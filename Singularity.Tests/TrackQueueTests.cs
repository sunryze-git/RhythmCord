using Singularity.Features.Music.Player;

namespace Singularity.Tests;

public class TrackQueueTests
{
    [Fact]
    public void Add_appends_in_order()
    {
        var q = new TrackQueue(10);
        q.Add([Tracks.Make("a"), Tracks.Make("b")], playNext: false);
        q.Add([Tracks.Make("c")], playNext: false);

        Assert.Equal(["a", "b", "c"], q.Items.Select(t => t.Title));
    }

    [Fact]
    public void PlayNext_inserts_at_the_front_keeping_the_given_order()
    {
        var q = new TrackQueue(10);
        q.Add([Tracks.Make("a"), Tracks.Make("b")], playNext: false);

        q.Add([Tracks.Make("n1"), Tracks.Make("n2")], playNext: true);

        Assert.Equal(["n1", "n2", "a", "b"], q.Items.Select(t => t.Title));
    }

    [Fact]
    public void Capacity_limits_what_is_accepted_and_keeps_the_first_tracks()
    {
        var q = new TrackQueue(3);

        Assert.Equal(3, q.Add(Tracks.Many("t", 5), playNext: false));
        Assert.Equal(["t0", "t1", "t2"], q.Items.Select(t => t.Title));
        Assert.Equal(0, q.Add([Tracks.Make("extra")], playNext: false));
    }

    [Fact]
    public void Partial_room_accepts_only_what_fits()
    {
        var q = new TrackQueue(4);
        q.Add(Tracks.Many("a", 3), playNext: false);

        Assert.Equal(1, q.Add(Tracks.Many("b", 3), playNext: false));
        Assert.Equal(4, q.Count);
    }

    [Fact]
    public void Dequeue_takes_from_the_front_and_reports_empty()
    {
        var q = new TrackQueue(5);
        q.Add(Tracks.Many("t", 2), playNext: false);

        Assert.Equal("t0", q.PeekNext()!.Title);
        Assert.True(q.TryDequeue(out var first));
        Assert.Equal("t0", first.Title);
        Assert.True(q.TryDequeue(out _));
        Assert.False(q.TryDequeue(out _));
        Assert.Null(q.PeekNext());
    }

    [Fact]
    public void Shuffle_keeps_every_track()
    {
        var q = new TrackQueue(100);
        q.Add(Tracks.Many("t", 50), playNext: false);

        q.Shuffle();

        Assert.Equal(Enumerable.Range(0, 50).Select(i => $"t{i}").Order(), q.Items.Select(t => t.Title).Order());
    }

    [Fact]
    public void Clear_empties_the_queue()
    {
        var q = new TrackQueue(5);
        q.Add(Tracks.Many("t", 3), playNext: false);

        q.Clear();

        Assert.Equal(0, q.Count);
    }
}
