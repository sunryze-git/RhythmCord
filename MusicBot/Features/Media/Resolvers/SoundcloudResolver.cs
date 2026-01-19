// SoundCloudNative
// This module resolves SoundCloud tracks using a native approach, using the API
// This code was directly rewritten from the original JavaScript implementation on the Cobalt.Tools project.
// https://cobalt.tools/
// https://github.com/imputnet/cobalt
// https://github.com/imputnet/cobalt/blob/main/api/src/processing/services/soundcloud.js

using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

using Microsoft.Extensions.Logging;

using MusicBot.Features.Media.Backends;
using MusicBot.Infrastructure;

namespace MusicBot.Features.Media.Resolvers;

public partial class SoundcloudResolver(HttpClient client, ILogger<SoundcloudResolver> logger) : IMediaResolver
{
    private static readonly CachedClientId _cachedId = new();

    private static readonly HashSet<string> _soundCloudHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "soundcloud.com", "on.soundcloud.com", "m.soundcloud.com"
    };

    public bool Enabled => true;
    public string Name => "SoundCloudNative";
    public int Priority => 1;

    public Task<bool> CanResolveAsync(string query)
    {
        if (!Uri.IsWellFormedUriString(query, UriKind.Absolute))
            return Task.FromResult(false);
        var uri = new Uri(query);
        return Task.FromResult(_soundCloudHosts.Contains(uri.Host));
    }

    public async Task<IReadOnlyList<MusicTrack>> ResolveAsync(string query)
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
            var duration = trackInfo.Duration > 0
                ? TimeSpan.FromMilliseconds(trackInfo.Duration)
                : (TimeSpan?)null;

            return new List<MusicTrack>
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

    public async Task<Stream> GetStreamAsync(MusicTrack video)
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

    public Task<bool> CanGetStreamAsync(MusicTrack video)
    {
        // We can get a stream for any valid SoundCloud track
        return Task.FromResult(video.Source == SongSource.SoundCloud);
    }

    private static string? GetBestThumbnailUrl(string? artworkUrl) => artworkUrl switch
    {
        null or { Length: 0 } => null,

        _ when artworkUrl.Contains("-large.jpg") || artworkUrl.Contains("-t500x500.jpg") => artworkUrl,

        _ => artworkUrl.Replace("-large.jpg", "-t500x500.jpg")
                .Replace("-t300x300.jpg", "-t500x500.jpg")
                .Replace("-t120x120.jpg", "-t500x500.jpg")
                .Replace("-small.jpg", "-t500x500.jpg") switch
            {
                var final when final == artworkUrl && !final.Contains('?') => $"{final}?size=500x500",
                var final => final
            }
    };

    private async Task<SoundCloudTrack?> GetTrackInfoAsync(string url, string clientId)
    {
        try
        {
            var uriBuilder = new UriBuilder("https://api-v2.soundcloud.com/resolve");
            var query = HttpUtility.ParseQueryString(string.Empty);
            query["url"] = url;
            query["client_id"] = clientId;
            uriBuilder.Query = query.ToString();

            var response = await client.GetStringAsync(uriBuilder.ToString());
            var track = JsonSerializer.Deserialize(response, SourceGenerationContext.Default.SoundCloudTrack);

            return track switch
            {
                { Policy: "BLOCK" } => LogAndReturn("SoundCloud track is blocked in this region"),
                { Policy: "SNIP" } => LogAndReturn("SoundCloud track is paid content"),
                { Media.Transcodings.Count: > 0 } => track,
                _ => LogAndReturn("No transcodings available for SoundCloud track")
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get SoundCloud track info");
            return null;
        }

        SoundCloudTrack? LogAndReturn(string message)
        {
            logger.LogWarning("{Message}", message);
            return null;
        }
    }

    private async Task<string?> ResolveUrlAsync(string url)
    {
        if (!url.Contains("on.soundcloud.com/", StringComparison.OrdinalIgnoreCase))
            return url;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            return response.Headers.Location?.ToString() ?? url;
        }
        catch
        {
            return url; // Return original if resolution fails
        }
    }

    private async Task<string?> FindClientIdAsync()
    {
        using var scope = logger.BeginScope("SoundCloud Client ID Discovery");
        try
        {
            LogFetchingMainPage();
            var response = await client.GetStringAsync("https://soundcloud.com/");
            if (VersionRegex().Match(response) is not { Success: true } vMatch)
            {
                LogVersionMatchFailed();
                return null;
            }

            var scVersion = vMatch.Groups[1].Value;
            LogFoundVersion(scVersion);

            if (_cachedId is { Id: { Length: > 0 } id, Version: var v } && v == scVersion)
            {
                LogUsingCache(id);
                return id;
            }

            var scriptUrls = ScriptTagRegex().Matches(response)
                .Select(m => m.Groups[1].Value)
                .Where(url => url.StartsWith("https://a-v2.sndcdn.com/"))
                .ToList();
            LogFoundScripts(scriptUrls.Count);

            foreach (var url in scriptUrls)
            {
                LogCheckingScript(url);
                var scriptContent = await client.GetStringAsync(url);

                var match = ClientIdRegex().Match(scriptContent);
                if (!match.Success)
                {
                    // If the script is large but contains the word "client_id",
                    // let's see the context of why the regex failed.
                    if (scriptContent.Contains("client_id", StringComparison.OrdinalIgnoreCase))
                    {
                        var index = scriptContent.IndexOf("client_id", StringComparison.OrdinalIgnoreCase);
                        var context = scriptContent[index..Math.Min(index + 100, scriptContent.Length)];
                        LogContextFailure(url, context);
                    }

                    continue;
                }

                var newId = match.Groups[1].Value;
                LogSuccess(newId, url);

                // Perform the update
                _cachedId.Version = scVersion;
                _cachedId.Id = newId;

                return newId; // Return the string, not the tuple
            }

            LogDiscoveryFailed();
            return null;
        }
        catch (Exception ex)
        {
            LogDiscoveryError(ex);
            return null;
        }
    }


    private async Task<string?> GetStreamUrlAsync(SoundCloudTrack track, string clientId)
    {
        try
        {
            if (track.Media is null) return null;

            var transcodings = track.Media.Transcodings ?? [];
            if (transcodings.Count == 0) return null;

            // Print all available transcodings to console
            var selectedStream = FindBestTranscoding(transcodings);
            if (selectedStream?.Url is null)
            {
                logger.LogWarning("No suitable transcodings found for SoundCloud track: {Title}", track.Title);
                return null;
            }

            var urilBuilder = new UriBuilder(selectedStream.Url);
            var query = HttpUtility.ParseQueryString(urilBuilder.Query);
            query["client_id"] = clientId;

            if (track.TrackAuthorization is { Length: > 0 } authorization) query["track_authorization"] = authorization;
            urilBuilder.Query = query.ToString();

            var response = await client.GetStringAsync(urilBuilder.ToString());
            var streamInfo = JsonSerializer.Deserialize(response, SourceGenerationContext.Default.SoundCloudStreamInfo);

            if (streamInfo?.Url is { Length: > 0 } streamUrl) return streamUrl;

            using var doc = JsonDocument.Parse(response);
            return doc.RootElement.TryGetProperty("url", out var url) ? url.GetString() : null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get SoundCloud stream URL");
            return null;
        }
    }

    private static SoundCloudTranscoding? FindBestTranscoding(List<SoundCloudTranscoding> transcodings) =>
        transcodings
            .Where(t => t is { Snipped: false, Format.Protocol: "progressive" }
                        && t.Format?.Protocol?.Contains("encrypted") != true)
            .OrderByDescending(t => t.Preset?.StartsWith("opus") ?? false)
            .ThenByDescending(t => t.Preset?.StartsWith("mp3") ?? false)
            .FirstOrDefault();

    [GeneratedRegex("""window\.__sc_version\s*=\s*["'](\d{10})["']""")]
    private static partial Regex VersionRegex();

    [GeneratedRegex("""<script.+src="([^"]+)">""")]
    private static partial Regex ScriptTagRegex();

    [GeneratedRegex("""client_id["']?\s*[:=]\s*["']([A-Za-z0-9]{32})["']""")]
    private static partial Regex ClientIdRegex();

    // logger stuffs
    [LoggerMessage(Level = LogLevel.Debug, Message = "Fetching SoundCloud main page for version discovery")]
    partial void LogFetchingMainPage();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not find window.__sc_version in SoundCloud HTML")]
    partial void LogVersionMatchFailed();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Detected SoundCloud version: {Version}")]
    partial void LogFoundVersion(string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Using cached SoundCloud Client ID: {ClientId}")]
    partial void LogUsingCache(string clientId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found {Count} potential asset scripts to check for Client ID")]
    partial void LogFoundScripts(int count);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Checking script for Client ID: {Url}")]
    partial void LogCheckingScript(string url);

    [LoggerMessage(Level = LogLevel.Trace, Message = "No Client ID found in script: {Url}")]
    partial void LogNoIdInScript(string url);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Successfully discovered new SoundCloud Client ID: {ClientId} from {SourceUrl}")]
    partial void LogSuccess(string clientId, string sourceUrl);

    [LoggerMessage(Level = LogLevel.Error, Message = "Exhausted all script URLs without finding a Client ID")]
    partial void LogDiscoveryFailed();

    [LoggerMessage(Level = LogLevel.Error, Message = "Critical error during SoundCloud Client ID discovery")]
    partial void LogDiscoveryError(Exception ex);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Found 'client_id' text but regex failed in {Url}. Context: {Context}")]
    partial void LogContextFailure(string url, string context);

    private class CachedClientId
    {
        public string Version { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
    }
}
