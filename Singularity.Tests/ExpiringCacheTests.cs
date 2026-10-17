using Microsoft.Extensions.Time.Testing;
using Singularity.Features.Music.Resolvers;

namespace Singularity.Tests;

public class ExpiringCacheTests
{
    [Fact]
    public void Entries_are_served_until_their_lifetime_ends()
    {
        var time = new FakeTimeProvider();
        var cache = new ExpiringCache<string>(TimeSpan.FromMinutes(10), time);
        cache.Set("k", "v");

        time.Advance(TimeSpan.FromMinutes(9));
        Assert.True(cache.TryGetFresh("k", out var v));
        Assert.Equal("v", v);

        time.Advance(TimeSpan.FromMinutes(2));
        Assert.False(cache.TryGetFresh("k", out _));
    }

    [Fact]
    public void Set_refreshes_the_lifetime_and_Remove_drops_the_entry()
    {
        var time = new FakeTimeProvider();
        var cache = new ExpiringCache<string>(TimeSpan.FromMinutes(10), time);
        cache.Set("k", "old");
        time.Advance(TimeSpan.FromMinutes(8));
        cache.Set("k", "new");
        time.Advance(TimeSpan.FromMinutes(8));

        Assert.True(cache.TryGetFresh("k", out var v));
        Assert.Equal("new", v);

        cache.Remove("k");
        Assert.False(cache.TryGetFresh("k", out _));
    }

    [Fact]
    public void Expired_entries_are_pruned_once_the_cache_grows()
    {
        var time = new FakeTimeProvider();
        var cache = new ExpiringCache<string>(TimeSpan.FromMinutes(1), time, pruneThreshold: 5);
        for (var i = 0; i < 5; i++) cache.Set($"old{i}", "x");

        time.Advance(TimeSpan.FromMinutes(2));
        cache.Set("fresh", "y");

        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGetFresh("fresh", out _));
    }
}
