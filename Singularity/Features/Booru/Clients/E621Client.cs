using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Singularity.Features.Booru.Models;

namespace Singularity.Features.Booru.Clients;

public interface IE621Client
{
    /// <summary>
    /// Searches e621 for posts matching the specified space-separated tags.
    /// </summary>
    Task<List<E621Post>> GetPostsAsync(string tags, int limit = 10, CancellationToken cancellationToken = default);
}

public class E621Client : IE621Client
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<E621Client> _logger;

    public E621Client(HttpClient httpClient, ILogger<E621Client> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _httpClient.BaseAddress ??= new Uri("https://e621.net/");

        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Singularity/1.0 (by sunryze on e621)");
        }
    }

    public async Task<List<E621Post>> GetPostsAsync(string tags, int limit = 10, CancellationToken cancellationToken = default)
    {
        var requestUrl = $"posts.json?tags={Uri.EscapeDataString(tags)}&limit={limit}";

        try
        {
            _logger.LogInformation("Querying e621 API with tags: {Tags} (Limit: {Limit})", tags, limit);

            var response = await _httpClient.GetFromJsonAsync(requestUrl, BooruJsonContext.Default.E621Response, cancellationToken);
            return response?.Posts ?? [];
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to fetch posts from e621 API.");
            return [];
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize response from e621 API.");
            return [];
        }
    }
}
