using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Singularity.Features.Booru.Models;

namespace Singularity.Features.Booru.Clients;

public interface IE621Client
{
    /// <summary>
    /// Searches e621 for posts matching the specified space-separated tags. An empty list means nothing matched;
    /// a failure to ask (e621 down, rate limiting, unreadable answer, timeout) throws <see cref="BooruUnavailableException" />.
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
        _httpClient.Timeout = TimeSpan.FromSeconds(15);

        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Singularity/1.0 (by sunryze on e621)");
        }
    }

    public async Task<List<E621Post>> GetPostsAsync(string tags, int limit = 10, CancellationToken cancellationToken = default)
    {
        var requestUrl = $"posts.json?tags={Uri.EscapeDataString(tags)}&limit={limit}";
        _logger.LogDebug("Querying e621 API with tags: {Tags} (Limit: {Limit})", tags, limit);

        try
        {
            var response = await _httpClient.GetFromJsonAsync(requestUrl, BooruJsonContext.Default.E621Response, cancellationToken);
            return response?.Posts ?? [];
        }
        catch (HttpRequestException ex)
        {
            throw new BooruUnavailableException(Describe(ex.StatusCode), ex);
        }
        catch (JsonException ex)
        {
            throw new BooruUnavailableException("e621 sent an answer I couldn't read.", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient timeout (the caller did not cancel).
            throw new BooruUnavailableException("e621 took too long to answer.", ex);
        }
    }

    // What the user is told, by what e621 said. The status code itself goes to the log through the inner exception.
    internal static string Describe(HttpStatusCode? status) => status switch
    {
        HttpStatusCode.TooManyRequests => "e621 is rate-limiting me right now. Try again in a minute.",
        HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => "e621 refused my request.",
        >= HttpStatusCode.InternalServerError => "e621 is having problems right now. Try again later.",
        null => "I couldn't reach e621.",
        _ => "e621 gave an unexpected answer."
    };
}
