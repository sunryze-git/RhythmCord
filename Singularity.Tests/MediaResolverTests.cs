using Microsoft.Extensions.Logging.Abstractions;
using Singularity.Features.Music;
using Singularity.Features.Music.Models;
using Singularity.Features.Music.Resolvers;
using Singularity.Features.Music.Services;
using Singularity.Infrastructure;
using Singularity.Observability;

namespace Singularity.Tests;

public class MediaResolverTests
{
    private sealed class FakeResolver(string name, SongSource source) : IMediaResolver
    {
        public string Name => name;
        public SongSource Source => source;
        public bool CanResolve { get; init; } = true;
        public Func<string, IReadOnlyList<MusicTrackNew>> Resolve { get; init; } = _ => [];
        public Func<string, int, IReadOnlyList<MusicTrackNew>> Search { get; init; } = (_, _) => [];
        public bool Streams { get; init; } = true;
        public Func<MusicTrackNew, Stream?> Stream { get; init; } = _ => new MemoryStream();
        public List<string> Queries { get; } = [];
        public List<int> SearchLimits { get; } = [];
        public int PreFetched { get; private set; }

        public Task<bool> CanResolveAsync(string query) => Task.FromResult(CanResolve);

        public Task<IReadOnlyList<MusicTrackNew>> ResolveAsync(string query)
        {
            Queries.Add(query);
            return Task.FromResult(Resolve(query));
        }

        public Task<IReadOnlyList<MusicTrackNew>> SearchAsync(string query, int limit)
        {
            Queries.Add(query);
            SearchLimits.Add(limit);
            return Task.FromResult(Search(query, limit));
        }

        public bool CanStream(MusicTrackNew track) => Streams;
        public Task<Stream?> GetStreamAsync(MusicTrackNew track) => Task.FromResult(Stream(track));
        public void PreFetch(MusicTrackNew track) => PreFetched++;
    }

    private static MediaResolver Build(BotMetrics metrics, string[]? order, params IMediaResolver[] resolvers) =>
        new(resolvers, new MusicOptions { ResolverOrder = order ?? ["a", "b"] }, metrics, NullLogger<MediaResolver>.Instance);

    private static IReadOnlyList<MusicTrackNew> Found(string title, SongSource source = SongSource.Ytdlp) =>
        [Tracks.Make(title, source)];

    [Fact]
    public async Task First_resolver_with_results_wins_and_is_counted()
    {
        var metrics = new BotMetrics();
        var a = new FakeResolver("a", SongSource.YouTube) { Resolve = _ => Found("from a", SongSource.YouTube) };
        var b = new FakeResolver("b", SongSource.Ytdlp) { Resolve = _ => Found("from b") };

        var result = await Build(metrics, null, a, b).ResolveSongsAsync("q");

        Assert.Equal("from a", Assert.Single(result).Title);
        Assert.Empty(b.Queries);
        Assert.Equal(1L, metrics.Aggregates.ByTag(MetricNames.Resolved)["a"]);
    }

    [Fact]
    public async Task Empty_results_and_failures_fall_through_to_the_next_resolver()
    {
        var a = new FakeResolver("a", SongSource.YouTube) { Resolve = _ => throw new InvalidOperationException("boom") };
        var b = new FakeResolver("b", SongSource.Ytdlp) { Resolve = _ => Found("from b") };

        var result = await Build(new BotMetrics(), null, a, b).ResolveSongsAsync("q");

        Assert.Equal("from b", Assert.Single(result).Title);
    }

    [Fact]
    public async Task Resolvers_that_decline_the_query_are_skipped()
    {
        var a = new FakeResolver("a", SongSource.YouTube) { CanResolve = false, Resolve = _ => Found("never") };
        var b = new FakeResolver("b", SongSource.Ytdlp) { Resolve = _ => Found("from b") };

        await Build(new BotMetrics(), null, a, b).ResolveSongsAsync("q");

        Assert.Empty(a.Queries);
    }

    [Fact]
    public async Task SearchException_reaches_the_caller()
    {
        var a = new FakeResolver("a", SongSource.YouTube) { Resolve = _ => throw new SearchException("not allowed") };
        var b = new FakeResolver("b", SongSource.Ytdlp) { Resolve = _ => Found("from b") };

        var ex = await Assert.ThrowsAsync<SearchException>(() => Build(new BotMetrics(), null, a, b).ResolveSongsAsync("q"));

        Assert.Equal("not allowed", ex.Message);
        Assert.Empty(b.Queries);
    }

    [Fact]
    public async Task Nothing_found_returns_empty_and_counts_it()
    {
        var metrics = new BotMetrics();

        var result = await Build(metrics, null, new FakeResolver("a", SongSource.YouTube), new FakeResolver("b", SongSource.Ytdlp))
            .ResolveSongsAsync("q");

        Assert.Empty(result);
        Assert.Equal(1, metrics.Aggregates.Total(MetricNames.ResolveNoResults));
        Assert.Equal(1, metrics.Aggregates.Total(MetricNames.ResolveRequests));
    }

    [Fact]
    public async Task Blank_queries_are_rejected()
    {
        await Assert.ThrowsAsync<SearchException>(() =>
            Build(new BotMetrics(), null, new FakeResolver("a", SongSource.YouTube)).ResolveSongsAsync("   "));
    }

    [Fact]
    public async Task Search_returns_the_first_resolvers_candidates_and_passes_the_limit_on()
    {
        var a = new FakeResolver("a", SongSource.YouTube) { Search = (_, limit) => Tracks.Many("hit", limit) };
        var b = new FakeResolver("b", SongSource.Ytdlp) { Search = (_, _) => Found("from b") };

        var result = await Build(new BotMetrics(), null, a, b).SearchAsync("q", 5);

        Assert.Equal(5, result.Count);
        Assert.Equal([5], a.SearchLimits);
        Assert.Empty(b.Queries);
    }

    [Fact]
    public async Task Search_falls_through_empty_results_and_failures()
    {
        var a = new FakeResolver("a", SongSource.YouTube) { Search = (_, _) => throw new InvalidOperationException("boom") };
        var b = new FakeResolver("b", SongSource.Ytdlp) { Search = (_, _) => Found("from b") };

        var result = await Build(new BotMetrics(), null, a, b).SearchAsync("q", 5);

        Assert.Equal("from b", Assert.Single(result).Title);
    }

    [Fact]
    public async Task Search_with_nothing_found_returns_empty()
    {
        var resolver = Build(new BotMetrics(), null, new FakeResolver("a", SongSource.YouTube));

        Assert.Empty(await resolver.SearchAsync("q", 5));
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("https://example.com/watch?v=1")]
    public async Task Search_rejects_blank_text_and_links(string query)
    {
        var a = new FakeResolver("a", SongSource.YouTube) { Search = (_, _) => Found("never") };

        await Assert.ThrowsAsync<SearchException>(() => Build(new BotMetrics(), null, a).SearchAsync(query, 5));

        Assert.Empty(a.Queries);
    }

    [Fact]
    public void Configured_order_is_respected_unknown_names_ignored_and_unlisted_resolvers_kept_last()
    {
        var a = new FakeResolver("a", SongSource.YouTube);
        var b = new FakeResolver("b", SongSource.Ytdlp);
        var c = new FakeResolver("c", SongSource.Ytdlp);

        var resolver = Build(new BotMetrics(), ["b", "nope", "a"], a, b, c);

        Assert.Equal(["b", "a", "c"], resolver.Resolvers.Select(r => r.Name));
    }

    [Fact]
    public async Task Stream_comes_from_the_resolver_that_produced_the_track()
    {
        var yt = new FakeResolver("a", SongSource.YouTube) { Stream = _ => new MemoryStream([1]) };
        var dlp = new FakeResolver("b", SongSource.Ytdlp) { Stream = _ => new MemoryStream([2]) };
        var resolver = Build(new BotMetrics(), ["b", "a"], dlp, yt); // yt-dlp listed first, but the track is a YouTube one

        await using var stream = await resolver.ResolveStreamAsync(Tracks.Make("t", SongSource.YouTube));

        Assert.Equal(1, stream!.ReadByte());
    }

    [Fact]
    public async Task Stream_falls_back_to_other_resolvers_when_the_owner_fails()
    {
        var metrics = new BotMetrics();
        var yt = new FakeResolver("a", SongSource.YouTube) { Stream = _ => throw new HttpRequestException("403") };
        var dlp = new FakeResolver("b", SongSource.Ytdlp) { Stream = _ => new MemoryStream([2]) };

        await using var stream = await Build(metrics, null, yt, dlp).ResolveStreamAsync(Tracks.Make("t", SongSource.YouTube));

        Assert.Equal(2, stream!.ReadByte());
        Assert.Equal(1, metrics.Aggregates.Total(MetricNames.StreamFallbacks));
    }

    [Fact]
    public async Task Stream_failure_names_the_reason_when_every_resolver_fails()
    {
        var yt = new FakeResolver("a", SongSource.YouTube) { Stream = _ => throw new ResolverFailedException("the video is private") };
        var dlp = new FakeResolver("b", SongSource.Ytdlp) { Streams = false };

        var ex = await Assert.ThrowsAsync<StreamUnavailableException>(
            () => Build(new BotMetrics(), null, yt, dlp).ResolveStreamAsync(Tracks.Make("t", SongSource.YouTube)));

        Assert.Equal("the video is private", ex.Message);
        Assert.IsType<ResolverFailedException>(ex.InnerException);
    }

    [Fact]
    public async Task Stream_failure_with_no_capable_resolver_says_so()
    {
        var none = new FakeResolver("a", SongSource.YouTube) { Streams = false };

        var ex = await Assert.ThrowsAsync<StreamUnavailableException>(
            () => Build(new BotMetrics(), null, none).ResolveStreamAsync(Tracks.Make("t")));

        Assert.Contains("no way to play", ex.Message);
    }

    [Fact]
    public async Task Nothing_found_is_not_the_same_as_could_not_look()
    {
        var metrics = new BotMetrics();
        var broken = new FakeResolver("a", SongSource.YouTube) { Resolve = _ => throw new HttpRequestException("down") };
        var empty = new FakeResolver("b", SongSource.Ytdlp); // ran fine, found nothing

        Assert.Empty(await Build(metrics, null, broken, empty).ResolveSongsAsync("q"));
        Assert.False(metrics.Aggregates.ByTag(MetricNames.Errors).ContainsKey("lookup.failed"));
    }

    [Fact]
    public async Task When_every_resolver_fails_the_lookup_is_reported_as_failed_with_a_reason()
    {
        var metrics = new BotMetrics();
        var a = new FakeResolver("a", SongSource.YouTube) { Resolve = _ => throw new HttpRequestException("down") };
        var b = new FakeResolver("b", SongSource.Ytdlp) { Resolve = _ => throw new ResolverFailedException("Video unavailable") };

        var ex = await Assert.ThrowsAsync<LookupFailedException>(() => Build(metrics, null, a, b).ResolveSongsAsync("q"));

        Assert.Contains("Video unavailable", ex.Message); // the most telling (last) reason
        Assert.IsAssignableFrom<UserFacingException>(ex);
        Assert.Equal(1, metrics.Aggregates.ByTag(MetricNames.Errors)["lookup.failed"]);
        Assert.Equal(0, metrics.Aggregates.Total(MetricNames.ResolveNoResults));
    }

    [Fact]
    public void PreFetch_only_involves_resolvers_of_the_tracks_source()
    {
        var yt = new FakeResolver("a", SongSource.YouTube);
        var dlp = new FakeResolver("b", SongSource.Ytdlp);

        Build(new BotMetrics(), null, yt, dlp).PreFetch(Tracks.Make("t", SongSource.YouTube));

        Assert.Equal(1, yt.PreFetched);
        Assert.Equal(0, dlp.PreFetched);
    }
}
