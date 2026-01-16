// OdesliSearcher
// This is a static class that is able to convert songs between different music services.

using System.Text.Json;

namespace MusicBot.Services.Media.Backends;

public class OdesliResult
{
    public string? PageUrl { get; set; }
    public Dictionary<string, string>? LinksByPlatform { get; set; }
}

public static class OdesliSearcher
{
    private static readonly HttpClient HttpClient = new();
    public static async Task<OdesliResult?> SearchAsync(string url)
    {
        var apiUrl = $"https://api.song.link/v1-alpha.1/links?url={Uri.EscapeDataString(url)}";
        var resp = await HttpClient.GetAsync(apiUrl);
        if (!resp.IsSuccessStatusCode)
            return null;
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var result = new OdesliResult();
        if (root.TryGetProperty("pageUrl", out var pageUrlProp))
            result.PageUrl = pageUrlProp.GetString();
        if (root.TryGetProperty("linksByPlatform", out var linksProp))
        {
            var dict = new Dictionary<string, string>();
            foreach (var platform in linksProp.EnumerateObject())
            {
                var urlProp = platform.Value.TryGetProperty("url", out var u) ? u.GetString() : null;
                if (!string.IsNullOrWhiteSpace(urlProp))
                    dict[platform.Name] = urlProp;
            }
            result.LinksByPlatform = dict;
        }
        return result;
    }
}