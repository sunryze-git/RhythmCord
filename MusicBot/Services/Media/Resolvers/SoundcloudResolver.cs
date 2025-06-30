// SoundCloudNative
// This module resolves SoundCloud tracks using a native approach, using the API
// This code was directly rewritten from the original JavaScript implementation on the Cobalt.Tools project.
// https://cobalt.tools/
// https://github.com/imputnet/cobalt
// https://github.com/imputnet/cobalt/blob/main/api/src/processing/services/soundcloud.js

using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MusicBot.Services.Media.Backends;
using MusicBot.Utilities;

namespace MusicBot.Services.Media.Resolvers;

public class SoundcloudResolver(HttpClient client, ILogger<SoundcloudResolver> logger) : IMediaResolver
{
    private static readonly CachedClientId CachedId = new();

    private static readonly HashSet<string> SoundCloudHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "soundcloud.com", "on.soundcloud.com", "m.soundcloud.com"
    };

    public string Name => "SoundCloudNative";
    public int Priority => 1;
    public Task<bool> CanResolveAsync(string query)
    {
        if (!Uri.IsWellFormedUriString(query, UriKind.Absolute))
            return Task.FromResult(false);
        var uri = new Uri(query);
        return Task.FromResult(SoundCloudHosts.Contains(uri.Host));
    }

    public async Task<IReadOnlyList<CustomSong>> ResolveAsync(string query)
    {
        try
        {
            var clientId = await FindClientIdAsync();
            if (string.IsNullOrEmpty(clientId))
            {
                logger.LogError("Failed to obtain SoundCloud client ID");
                return [];
            }

            var resolvedUrl = await ResolveUrlAsync(query);
            if (string.IsNullOrEmpty(resolvedUrl))
            {
                logger.LogError("Failed to resolve SoundCloud URL: {Query}", query);
                return [];
            }

            var trackInfo = await GetTrackInfoAsync(resolvedUrl, clientId);
            if (trackInfo == null)
            {
                logger.LogError("Failed to get track info for: {Url}", resolvedUrl);
                return [];
            }

            var thumbnailUrl = GetBestThumbnailUrl(trackInfo.ArtworkUrl);
            var duration = trackInfo.Duration > 0 ? TimeSpan.FromMilliseconds(trackInfo.Duration) : (TimeSpan?)null;

            return new List<CustomSong>
            {
                new(query,
                    query,
                    trackInfo.Title?.Trim() ?? "Unknown Title",
                    trackInfo.User?.Username?.Trim() ?? "Unknown Artist",
                    duration ?? TimeSpan.Zero, 
                    thumbnailUrl ?? string.Empty, 
                    SongSource.SoundCloud)
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SoundCloud resolution failed for: {Query}", query);
            return [];
        }
    }

    public async Task<Stream> GetStreamAsync(CustomSong video)
    {
        if (video.Source != SongSource.SoundCloud)
            throw new Exception($"Cannot stream non-SoundCloud song with SoundcloudResolver: {video.Title}");
        // Fetch the stream for the given video (CustomSong) on demand
        var clientId = await FindClientIdAsync();
        if (string.IsNullOrEmpty(clientId))
            throw new InvalidOperationException("Failed to obtain SoundCloud client ID");

        // Re-resolve the track info using the video URL
        var trackInfo = await GetTrackInfoAsync(video.Url, clientId);
        if (trackInfo == null)
            throw new InvalidOperationException("Failed to get track info for: " + video.Url);

        var streamUrl = await GetStreamUrlAsync(trackInfo, clientId);
        if (string.IsNullOrEmpty(streamUrl))
            throw new InvalidOperationException("Failed to get stream URL for track: " + trackInfo.Title);

        return await client.GetStreamAsync(streamUrl);
    }

    public Task<bool> CanGetStreamAsync(CustomSong video)
    {
        // We can get a stream for any valid SoundCloud track
        return Task.FromResult(video.Source == SongSource.SoundCloud);
    }

    private static string? GetBestThumbnailUrl(string? artworkUrl)
    {
        if (string.IsNullOrEmpty(artworkUrl))
            return null;

        // SoundCloud provides different sizes by modifying the URL
        // Default is usually 100x100, we want higher resolution
    
        // Try to get the highest quality version
        if (artworkUrl.Contains("-large.jpg"))
            return artworkUrl; // Already high quality
    
        if (artworkUrl.Contains("-t500x500.jpg"))
            return artworkUrl; // Already 500x500
    
        // Replace common size patterns with high quality version
        var highQualityUrl = artworkUrl
            .Replace("-large.jpg", "-t500x500.jpg")
            .Replace("-t300x300.jpg", "-t500x500.jpg")
            .Replace("-t120x120.jpg", "-t500x500.jpg")
            .Replace("-small.jpg", "-t500x500.jpg");

        // If no replacement was made, try appending the size parameter
        if (highQualityUrl == artworkUrl && !artworkUrl.Contains('?'))
        {
            // Some URLs don't have size in filename, try URL parameter
            highQualityUrl = $"{artworkUrl}?size=500x500";
        }

        return highQualityUrl;
    }

    private async Task<SoundCloudTrack?> GetTrackInfoAsync(string url, string clientId)
    {
        try
        {
            var resolveUrl = new UriBuilder("https://api-v2.soundcloud.com/resolve");
            var query = System.Web.HttpUtility.ParseQueryString(string.Empty);
            query["url"] = url;
            query["client_id"] = clientId;
            resolveUrl.Query = query.ToString();

            var response = await client.GetStringAsync(resolveUrl.ToString());
            var track = JsonSerializer.Deserialize<SoundCloudTrack>(response, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            });

            switch (track?.Policy)
            {
                // Validate track
                case "BLOCK":
                    logger.LogWarning("SoundCloud track is blocked in this region");
                    return null;
                case "SNIP":
                    logger.LogWarning("SoundCloud track is paid content");
                    return null;
            }

            if (track?.Media?.Transcodings != null && track.Media.Transcodings.Count != 0) return track;
            logger.LogWarning("No transcodings available for SoundCloud track");
            return null;

        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get SoundCloud track info");
            return null;
        }
    }

    private async Task<string?> ResolveUrlAsync(string url)
    {
        try
        {
            // Handle short links
            if (!url.Contains("on.soundcloud.com/")) return url;
            var response = await client.GetAsync(url);
            if (response.Headers.Location != null)
            {
                url = response.Headers.Location.ToString();
            }

            return url;
        }
        catch
        {
            return url; // Return original if resolution fails
        }
    }

    private async Task<string?> FindClientIdAsync()
    {
        try
        {
            var response = await client.GetStringAsync("https://soundcloud.com/");
            var versionMatch = Regex.Match(response, """<script>window\.__sc_version="(\d{10})"</script>""");
            
            if (!versionMatch.Success)
                return null;

            var scVersion = versionMatch.Groups[1].Value;

            if (CachedId.Version == scVersion && !string.IsNullOrEmpty(CachedId.Id))
            {
                return CachedId.Id;
            }

            var scriptMatches = Regex.Matches(response, """<script.+src="(.+)">""");
            
            foreach (Match scriptMatch in scriptMatches)
            {
                var url = scriptMatch.Groups[1].Value;
                
                if (!url.StartsWith("https://a-v2.sndcdn.com/"))
                    continue;

                var scriptContent = await client.GetStringAsync(url);
                var clientIdMatch = Regex.Match(scriptContent, """\("client_id=([A-Za-z0-9]{32})"\)""");

                if (!clientIdMatch.Success) continue;
                var clientId = clientIdMatch.Groups[1].Value;
                CachedId.Version = scVersion;
                CachedId.Id = clientId;
                return clientId;
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to find SoundCloud client ID");
            return null;
        }
    }


    private async Task<string?> GetStreamUrlAsync(SoundCloudTrack track, string clientId)
    {
        try
        {
            if (track.Media?.Transcodings is null)
                return null;
            
            // Print all available transcodings to console
            Console.WriteLine($"\n=== Available streams for: {track.Title} ===");
            Console.WriteLine("Progressive streams:");
            var progressiveStreams = track.Media.Transcodings
                .Where(t => t.Format?.Protocol == "progressive")
                .ToList();
        
            if (progressiveStreams.Any())
            {
                foreach (var stream in progressiveStreams)
                {
                    Console.WriteLine($"  - Preset: {stream.Preset}, Snipped: {stream.Snipped}, Encrypted: {stream.Format?.Protocol?.Contains("encrypted")}");
                }
            }
            else
            {
                Console.WriteLine("  No progressive streams available");
            }

            Console.WriteLine("HLS streams:");
            var hlsStreams = track.Media.Transcodings
                .Where(t => t.Format?.Protocol == "hls")
                .ToList();
        
            if (hlsStreams.Any())
            {
                foreach (var stream in hlsStreams)
                {
                    Console.WriteLine($"  - Preset: {stream.Preset}, Snipped: {stream.Snipped}, Encrypted: {stream.Format?.Protocol?.Contains("encrypted")}");
                }
            }
            else
            {
                Console.WriteLine("  No HLS streams available");
            }

            Console.WriteLine("=== End of available streams ===\n");
            
            var selectedStream = FindBestTranscoding(track.Media.Transcodings);
            if (selectedStream?.Url is null)
            {
                logger.LogWarning("No suitable transcodings found for SoundCloud track: {Title}", track.Title);
                return null;
            }
            
            logger.LogDebug("Selected transcoding: {Preset} with protocol {Protocol}",
                selectedStream.Preset, selectedStream.Format?.Protocol);
            
            var fileUrl = new UriBuilder(selectedStream.Url);
            var query = System.Web.HttpUtility.ParseQueryString(fileUrl.Query);
            query["client_id"] = clientId;
            if (!string.IsNullOrEmpty(track.TrackAuthorization))
            {
                query["track_authorization"] = track.TrackAuthorization;
            }
            fileUrl.Query = query.ToString();
            logger.LogDebug("Requesting stream info from: {Url}", fileUrl.ToString());

            var response = await client.GetStringAsync(fileUrl.ToString());
            logger.LogDebug("Stream info response: {Response}", response);
            
            var streamInfo = JsonSerializer.Deserialize<SoundCloudStreamInfo>(response, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                PropertyNameCaseInsensitive = true
            });

            if (streamInfo?.Url != null) return streamInfo.Url;
            
            logger.LogWarning("Stream info deserialization failed or returned null URL");
            
            // Try parsing as dynamic to see the actual structure
            using var doc = JsonDocument.Parse(response);
            return doc.RootElement.TryGetProperty("url", out var urlElement) 
                ? urlElement.GetString() 
                : streamInfo?.Url;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get SoundCloud stream URL");
            return null;
        }
    }
    
    private static SoundCloudTranscoding? FindBestForPreset(List<SoundCloudTranscoding> transcodings, string preset)
    {
        return transcodings
            .FirstOrDefault(t => t is { Snipped: false, Format.Protocol: "progressive" } &&
                                 t.Format?.Protocol?.Contains("encrypted") != true &&
                                 t.Preset?.StartsWith($"{preset}_") == true);
    }
    
    private static SoundCloudTranscoding? FindBestTranscoding(List<SoundCloudTranscoding> transcodings)
    {
        // Prefer OPUS, fallback to MP3
        var opusTranscoding = FindBestForPreset(transcodings, "opus");
        var mp3Transcoding = FindBestForPreset(transcodings, "mp3");

        return opusTranscoding ?? mp3Transcoding;
    }
    
    private class CachedClientId
    {
        public string Version { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
    }
}