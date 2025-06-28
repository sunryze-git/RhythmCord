using System.Text.RegularExpressions;
using System.Text.Json;
using System.Security.Cryptography;
using MusicBot.Utilities;
using YoutubeExplode.Videos;
using YoutubeExplode.Common;

namespace MusicBot.Services.Media.Resolvers;

public class SpotifyResolver : IMediaResolver
{
    public string Name => "Spotify";
    public int Priority => 3;
    private static readonly HttpClient HttpClient = new(); // Renamed for convention
    private const string TokenUrl = "https://open.spotify.com/api/token";
    private const string PlaylistBaseUrl = "https://api.spotify.com/v1/playlists/{0}/tracks?limit=100&additional_types=track";
    private const string TrackBaseUrl = "https://api.spotify.com/v1/tracks/{0}";
    private const string AlbumBaseUrl = "https://api.spotify.com/v1/albums/{0}/tracks";
    private static readonly byte[] TotpSecret = "5507145853487499592248630329347"u8.ToArray();

    public Task<bool> CanResolveAsync(string query)
    {
        try
        {
            var info = ParseSpotifyUri(query);
            return Task.FromResult(info != null);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public async Task<IReadOnlyList<IVideo>> ResolveAsync(string query)
    {
        var info = ParseSpotifyUri(query);
        if (info == null)
            throw new Exception("Invalid Spotify URL or ID");
        var (type, id) = info.Value;

        var (totp, timestamp) = GenerateTotp();
        var token = await GetSpotifyTokenAsync(totp, timestamp);
        var songs = new List<IVideo>();
        switch (type)
        {
            case "playlist":
            {
                string? url = string.Format(PlaylistBaseUrl, id);
                while (url != null)
                {
                    var resp = await GetJsonFromApiAsync(url, token);
                    foreach (var item in resp.RootElement.GetProperty("items").EnumerateArray())
                    {
                        if (item.TryGetProperty("track", out var trackProp))
                        {
                            var track = ParseTrack(trackProp);
                            if (!string.IsNullOrWhiteSpace(track))
                            {
                                var title = trackProp.GetProperty("name").GetString() ?? "Unknown Title";
                                var artist = trackProp.GetProperty("artists")[0].GetProperty("name").GetString() ?? "Unknown Artist";
                                var urlStr = trackProp.TryGetProperty("external_urls", out var extUrls) && extUrls.TryGetProperty("spotify", out var spotifyUrl) ? spotifyUrl.GetString() : null;
                                var thumbnails = ParseThumbnails(trackProp);
                                songs.Add(new CustomSong(urlStr ?? $"https://open.spotify.com/track/{trackProp.GetProperty("id").GetString()}", title, artist, thumbnails, Stream.Null)
                                {
                                    // Set Duration property via object initializer
                                    FormatType = null,
                                    // Duration is set via constructor in CustomSong, so you may need to update CustomSong to accept duration
                                });
                            }
                        }
                    }
                    url = resp.RootElement.TryGetProperty("next", out var nextProp) && nextProp.ValueKind != JsonValueKind.Null ? nextProp.GetString() : null;
                }
                break;
            }
            case "track":
            {
                var resp = await GetJsonFromApiAsync(string.Format(TrackBaseUrl, id), token);
                var track = ParseTrack(resp.RootElement);
                if (!string.IsNullOrWhiteSpace(track))
                {
                    var title = resp.RootElement.GetProperty("name").GetString() ?? "Unknown Title";
                    var artist = resp.RootElement.GetProperty("artists")[0].GetProperty("name").GetString() ?? "Unknown Artist";
                    var urlStr = resp.RootElement.TryGetProperty("external_urls", out var extUrls) && extUrls.TryGetProperty("spotify", out var spotifyUrl) ? spotifyUrl.GetString() : null;
                    var thumbnails = ParseThumbnails(resp.RootElement);
                    songs.Add(new CustomSong(urlStr ?? $"https://open.spotify.com/track/{resp.RootElement.GetProperty("id").GetString()}", title, artist, thumbnails, Stream.Null)
                    {
                        FormatType = null,
                        // Duration is set via constructor in CustomSong, so you may need to update CustomSong to accept duration
                    });
                }
                break;
            }
            case "album":
            {
                var resp = await GetJsonFromApiAsync(string.Format(AlbumBaseUrl, id), token);
                foreach (var item in resp.RootElement.GetProperty("items").EnumerateArray())
                {
                    var track = ParseTrack(item);
                    if (!string.IsNullOrWhiteSpace(track))
                    {
                        var title = item.GetProperty("name").GetString() ?? "Unknown Title";
                        var artist = item.GetProperty("artists")[0].GetProperty("name").GetString() ?? "Unknown Artist";
                        var urlStr = item.TryGetProperty("external_urls", out var extUrls) && extUrls.TryGetProperty("spotify", out var spotifyUrl) ? spotifyUrl.GetString() : null;
                        var thumbnails = ParseThumbnails(item);
                        songs.Add(new CustomSong(urlStr ?? $"https://open.spotify.com/track/{item.GetProperty("id").GetString()}", title, artist, thumbnails, Stream.Null)
                        {
                            FormatType = null,
                            // Duration is set via constructor in CustomSong, so you may need to update CustomSong to accept duration
                        });
                    }
                }
                break;
            }
        }
        return songs;
    }

    public Task<Stream> GetStreamAsync(IVideo video)
    {
        throw new NotImplementedException($"[DRM] It is not possible to stream directly from Spotify.");
    }

    // Helper methods and record for Spotify URI parsing
    private static (string Type, string Id)? ParseSpotifyUri(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;
        uri = uri.Trim();
        var embedMatch = Regex.Match(uri, @"embed.spotify.com.*[?&]uri=spotify:([a-z]+):([a-zA-Z0-9]+)");
        if (embedMatch.Success)
            return (embedMatch.Groups[1].Value, embedMatch.Groups[2].Value);
        var urlMatch = Regex.Match(uri, @"open\.spotify\.com/(playlist|track|album)/([a-zA-Z0-9]+)");
        if (urlMatch.Success)
            return (urlMatch.Groups[1].Value, urlMatch.Groups[2].Value);
        var uriMatch = Regex.Match(uri, @"spotify:(playlist|track|album):([a-zA-Z0-9]+)");
        if (uriMatch.Success)
            return (uriMatch.Groups[1].Value, uriMatch.Groups[2].Value);
        // fallback: just an ID, assume playlist
        if (Regex.IsMatch(uri, @"^[a-zA-Z0-9]{22,}$"))
            return ("playlist", uri);
        return null;
    }

    private static (string, long) GenerateTotp()
    {
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        using var hmac = new HMACSHA1(TotpSecret);
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(counterBytes);
        var hash = hmac.ComputeHash(counterBytes);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) |
                     ((hash[offset + 1] & 0xFF) << 16) |
                     ((hash[offset + 2] & 0xFF) << 8) |
                     (hash[offset + 3] & 0xFF);
        var totp = (binary % 1000000).ToString("D6");
        return (totp, counter * 30000);
    }

    private static async Task<string> GetSpotifyTokenAsync(string totp, long timestamp)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, TokenUrl + $"?reason=init&productType=web-player&totp={totp}&totpVer=5&ts={timestamp}");
        req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        req.Headers.Add("Accept", "application/json");
        req.Headers.Add("Accept-Language", "en-US,en;q=0.9");
        req.Headers.Add("Referer", "https://open.spotify.com/");
        req.Headers.Add("Origin", "https://open.spotify.com");
        var resp = await HttpClient.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var accessToken = doc.RootElement.GetProperty("accessToken").GetString();
        if (string.IsNullOrEmpty(accessToken))
            throw new Exception("Spotify access token not found in response.");
        return accessToken;
    }

    private static async Task<JsonDocument> GetJsonFromApiAsync(string url, string accessToken)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("Authorization", $"Bearer {accessToken}");
        req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        req.Headers.Add("Accept", "application/json");
        var resp = await HttpClient.SendAsync(req);
        if ((int)resp.StatusCode == 429)
        {
            var retryAfter = resp.Headers.RetryAfter?.Delta?.Seconds ?? 5;
            await Task.Delay((retryAfter + 1) * 1000);
            resp = await HttpClient.SendAsync(req);
        }
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json);
    }

    private static string? ParseTrack(JsonElement track)
    {
        if (!track.TryGetProperty("artists", out var artists) || artists.GetArrayLength() == 0)
            return null;
        var artist = artists[0].GetProperty("name").GetString();
        var title = track.GetProperty("name").GetString();
        var full = $"{artist} {title}";
        return string.IsNullOrWhiteSpace(full) ? null : Regex.Replace(full, @"\([^)]*\)", "").Trim();
    }

    private static IReadOnlyList<Thumbnail> ParseThumbnails(JsonElement trackOrAlbum)
    {
        // Try to get images from album property, fallback to images directly on the object
        JsonElement images;
        if (trackOrAlbum.TryGetProperty("album", out var album) && album.TryGetProperty("images", out images))
        {
            return images.EnumerateArray().Select(img =>
                new Thumbnail(
                    img.GetProperty("url").GetString() ?? string.Empty,
                    new Resolution(
                        img.TryGetProperty("width", out var w) ? w.GetInt32() : 0,
                        img.TryGetProperty("height", out var h) ? h.GetInt32() : 0
                    ))).ToList();
        }
        else if (trackOrAlbum.TryGetProperty("images", out images))
        {
            return images.EnumerateArray().Select(img =>
                new Thumbnail(
                    img.GetProperty("url").GetString() ?? string.Empty,
                    new Resolution(
                        img.TryGetProperty("width", out var w) ? w.GetInt32() : 0,
                        img.TryGetProperty("height", out var h) ? h.GetInt32() : 0
                    ))).ToList();
        }
        return new List<Thumbnail>();
    }
}