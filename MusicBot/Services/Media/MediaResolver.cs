using Microsoft.Extensions.Logging;
using YoutubeExplode.Common;
using YoutubeExplode.Videos;
using CobaltApi;
using MusicBot.Utilities;

namespace MusicBot.Services.Media;

public class MediaResolver(
    ILogger<MediaResolver> logger,
    SearchService searchService,
    YoutubeService youtubeService,
    CobaltClient cobaltClient)
{
    private static readonly string[] AudioFileTypes = [".mp3", ".wav", ".flac", ".ogg", ".opus"];

    public async Task<Stream> ResolveStreamAsync(IVideo video)
    {
        if (video is CustomSong customSong)
        {
            return customSong.Source;
        }

        var request = new Request { url = video.Url, audioFormat = "best" };
        var videoInfo = await cobaltClient.GetCobaltResponseAsync(request);
        return await cobaltClient.GetTunnelStreamAsync(videoInfo);
    }

    public async Task<IReadOnlyList<IVideo>> ResolveSongsAsync(string query)
    { 
        // Option 1: Search term is not a URL.
        if (!Uri.IsWellFormedUriString(query, UriKind.Absolute))
        {
            try
            {
                var video = await youtubeService.GetVideoAsync(query);
                return video != null ? new List<IVideo> { video } : Array.Empty<IVideo>();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "YouTube search failed.");
                throw;
            }
        }
        var uri = new Uri(query);
            
        // Option 2: Search term is a direct URL.
        if (IsAudioFile(uri))
        {
            var name = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
            var stream = await searchService.GetStreamFromUri(uri);
            return new List<IVideo> { new CustomSong(query, name, null, null, stream) };
        }
        
        // Option 3: Attempt to get video from Cobalt API.
        try
        {
            var request = new Request { url = uri.ToString(), audioFormat = "opus" };
            var video = await cobaltClient.GetCobaltResponseAsync(request);
            var stream = await cobaltClient.GetTunnelStreamAsync(video);
            return new List<IVideo> { new CustomSong(query, video.Title, video.Artist, new List<Thumbnail> { new(string.Empty, new Resolution()) }, stream) };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Cobalt API video retrieval failed.");
        }
        
        // Option 4: Attempt search with YouTubeExplode.
        if (uri.Authority.EndsWith("youtube.com", StringComparison.InvariantCulture))
        {
            if (uri.AbsolutePath == "/playlist")
            {
                var playlist = await youtubeService.GetPlaylistVideosAsync(uri.AbsoluteUri);
                return playlist.Count == 0 ? Array.Empty<IVideo>() : playlist;
            }

            var video = await youtubeService.GetVideoAsync(uri.AbsoluteUri);
            return video == null ? Array.Empty<IVideo>() : new List<IVideo> { video };
        }
        
        // Option 5: Attempt search with YT-DLP.
        logger.LogInformation("Querying song data via yt-dlp.");
        var metadata = await searchService.GetMetadataAsync(query);
        if (metadata.Any())
            return metadata;
        
        // If we reach here, no valid song was found.
        logger.LogWarning("No valid song found for the given term.");
        return [];
    }

    private static bool IsAudioFile(Uri url)
    {
        try
        {
            var ext = Path.GetExtension(url.LocalPath).ToLowerInvariant();
            return Array.Exists(AudioFileTypes, x => x == ext);
        }
        catch (UriFormatException)
        {
            return false;
        }
    }
}