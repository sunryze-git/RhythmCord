using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Singularity.Features.Music;
using Singularity.Features.Music.Models;
using Singularity.Features.Music.Resolvers;
using Singularity.Observability;

namespace Singularity.Tests;

public class StderrTailTests
{
    [Theory]
    [InlineData("ERROR: [youtube] dQw4w9WgXcQ: Video unavailable. This video is private", "Video unavailable. This video is private")]
    [InlineData("\e[0;31mERROR:\e[0m [youtube] abc: Sign in to confirm you're not a bot", "Sign in to confirm you're not a bot")]
    [InlineData("ERROR: Unsupported URL: https://example.com/x", "Unsupported URL: https://example.com/x")]
    public void The_error_line_is_cleaned_for_display(string line, string expected) =>
        Assert.Equal(expected, StderrTail.Describe([line], 1));

    [Fact]
    public void The_error_line_is_preferred_over_later_noise_and_the_last_error_wins()
    {
        string[] lines = ["WARNING: something", "ERROR: first problem", "[download] 10%", "ERROR: second problem", "trailing chatter"];

        Assert.Equal("second problem", StderrTail.Describe(lines, 1));
    }

    [Fact]
    public void Without_an_error_line_the_last_line_is_used_and_without_any_the_exit_code()
    {
        Assert.Equal("just this", StderrTail.Describe(["", "just this"], 2));
        Assert.Equal("yt-dlp exited with code 7", StderrTail.Describe([], 7));
        Assert.Equal("yt-dlp exited with code 7", StderrTail.Describe(["ERROR:   "], 7));
    }

    [Fact]
    public void Very_long_messages_are_truncated()
    {
        var reason = StderrTail.Describe(["ERROR: " + new string('x', 500)], 1);

        Assert.Equal(200, reason.Length);
        Assert.EndsWith("…", reason);
    }

    [Fact]
    public void The_tail_keeps_only_the_most_recent_lines()
    {
        var tail = new StderrTail(capacity: 2);
        tail.Add("ERROR: old");
        tail.Add("noise 1");
        tail.Add("noise 2");

        Assert.Equal("noise 2", tail.Reason(1)); // the old ERROR line has been pushed out
    }
}

/// <summary>Runs the real resolver and stream code against a fake "yt-dlp" script on PATH.</summary>
[Collection("PATH")]
public sealed class YtdlpFailureTests : IDisposable
{
    private const string Script = """
        #!/bin/sh
        [ -n "$FAKE_ARGLOG" ] && printf '%s\n' "$@" >> "$FAKE_ARGLOG"
        all="$*"
        case "$all" in
          *--dump-single-json*)
            case "$all" in
              *ytsearch1:ok*)   echo '{"title":"T","webpage_url":"https://x.test/1","id":"1"}'; exit 0;;
              *ytsearch1:none*) echo '{"entries":[]}'; exit 0;;
              *ytsearch1:fail*) echo "ERROR: [youtube] abc: Video unavailable. This video is private" >&2; exit 1;;
              *ytsearch1:hang*) sleep 30;;
            esac;;
          *)
            case "$all" in
              */bad*)        echo "ERROR: [youtube] abc: Sign in to confirm you're not a bot" >&2; exit 1;;
              */good*)       printf 'AUDIO'; exit 0;;
              */partial*)    printf 'AUD'; echo "ERROR: Connection reset by peer" >&2; exit 1;;
              */hangstream*) sleep 30;;
            esac;;
        esac
        exit 0
        """;

    private readonly string _dir = Directory.CreateTempSubdirectory("fake-ytdlp").FullName;
    private readonly string _originalPath = Environment.GetEnvironmentVariable("PATH") ?? "";
    private readonly string _argLog;
    private readonly BotMetrics _metrics = new();

    public YtdlpFailureTests()
    {
        var path = Path.Combine(_dir, "yt-dlp");
        File.WriteAllText(path, Script.Replace("\r\n", "\n"));
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        _argLog = Path.Combine(_dir, "args.log");
        Environment.SetEnvironmentVariable("FAKE_ARGLOG", _argLog);
        Environment.SetEnvironmentVariable("PATH", _dir + Path.PathSeparator + _originalPath);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _originalPath);
        Environment.SetEnvironmentVariable("FAKE_ARGLOG", null);
        Directory.Delete(_dir, recursive: true);
    }

    private YtdlpResolver Resolver(TimeSpan? timeout = null) =>
        new(NullLogger<YtdlpResolver>.Instance, new MusicOptions { YtdlpTimeout = timeout ?? TimeSpan.FromSeconds(10) }, _metrics);

    private static MusicTrackNew Track(string path) => Tracks.Make("t") is var t
        ? new MusicTrackNew
        {
            Query = "q",
            Url = "http://8.8.8.8/" + path,
            Title = "t",
            Author = "a",
            Duration = null,
            ThumbnailUrl = null,
            Source = SongSource.Ytdlp
        }
        : t;

    private static async Task<string> ReadAllAsync(Stream stream)
    {
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    // ---------------------------------------------------------------- looking things up

    [Fact]
    public async Task A_successful_search_returns_the_track()
    {
        var track = Assert.Single(await Resolver().ResolveAsync("ok"));

        Assert.Equal("T", track.Title);
    }

    [Fact]
    public async Task A_search_that_finds_nothing_is_empty_not_an_error()
    {
        Assert.Empty(await Resolver().ResolveAsync("none"));
    }

    [Fact]
    public async Task A_yt_dlp_failure_carries_yt_dlps_own_reason_cleaned_up()
    {
        var ex = await Assert.ThrowsAsync<ResolverFailedException>(() => Resolver().ResolveAsync("fail"));

        Assert.Equal("Video unavailable. This video is private", ex.Message);
    }

    [Fact]
    public async Task A_yt_dlp_that_hangs_is_killed_and_reported_as_too_slow()
    {
        var sw = Stopwatch.StartNew();

        var ex = await Assert.ThrowsAsync<ResolverFailedException>(() => Resolver(TimeSpan.FromMilliseconds(800)).ResolveAsync("hang"));

        Assert.Contains("too long", ex.Message);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), "should not wait for the 30 s sleep");
    }

    [Fact]
    public async Task Hostile_text_reaches_yt_dlp_as_one_argument_after_a_double_dash()
    {
        const string hostile = "x\" --exec \"touch /tmp/pwned";

        // The fake prints nothing for this query, so the lookup fails; what matters is what yt-dlp was *called with*.
        await Assert.ThrowsAsync<ResolverFailedException>(() => Resolver().ResolveAsync(hostile));

        var args = File.ReadAllLines(_argLog);
        Assert.Contains($"ytsearch1:{hostile}", args);                       // the whole thing is a single argument
        Assert.DoesNotContain("--exec", args);                                  // never parsed as an option
        Assert.Equal("--", args[^2]);                                           // options end before the query
    }

    [Fact]
    public async Task A_yt_dlp_that_succeeds_with_unreadable_output_is_a_failure_not_an_exception_leak()
    {
        var ex = await Assert.ThrowsAsync<ResolverFailedException>(() => Resolver().ResolveAsync("silent"));

        Assert.Contains("couldn't read", ex.Message);
        Assert.IsAssignableFrom<System.Text.Json.JsonException>(ex.InnerException);
    }

    [Fact]
    public async Task Private_addresses_are_refused_before_yt_dlp_is_even_started()
    {
        await Assert.ThrowsAsync<SearchException>(() => Resolver().ResolveAsync("http://169.254.169.254/latest/meta-data"));

        Assert.False(File.Exists(_argLog));
    }

    // ---------------------------------------------------------------- streaming

    [Fact]
    public async Task A_good_stream_reads_to_the_end_without_error()
    {
        await using var stream = (await Resolver().GetStreamAsync(Track("good")))!;

        Assert.Equal("AUDIO", await ReadAllAsync(stream));
    }

    [Fact]
    public async Task A_failed_download_surfaces_yt_dlps_reason_instead_of_looking_like_a_short_track()
    {
        await using var stream = (await Resolver().GetStreamAsync(Track("bad")))!;

        var ex = await Assert.ThrowsAsync<MediaStreamException>(() => ReadAllAsync(stream));

        Assert.Contains("Sign in to confirm you're not a bot", ex.Message);
        Assert.StartsWith("the download failed", ex.Message);
    }

    [Fact]
    public async Task A_download_that_breaks_part_way_delivers_what_it_had_then_fails()
    {
        await using var stream = (await Resolver().GetStreamAsync(Track("partial")))!;
        var buffer = new byte[16];

        var first = await stream.ReadAsync(buffer);
        var ex = await Assert.ThrowsAsync<MediaStreamException>(async () => { while (await stream.ReadAsync(buffer) > 0) { } });

        Assert.Equal("AUD", System.Text.Encoding.ASCII.GetString(buffer, 0, first));
        Assert.Contains("Connection reset by peer", ex.Message);
    }

    [Fact]
    public async Task Disposing_the_stream_kills_the_download_and_releases_the_active_count()
    {
        var stream = (await Resolver().GetStreamAsync(Track("hangstream")))!;
        Assert.Equal(1, _metrics.Aggregates.Total(MetricNames.YtdlpActive));

        var sw = Stopwatch.StartNew();
        await stream.DisposeAsync();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), "dispose must not wait for the 30 s sleep");
        Assert.Equal(0, _metrics.Aggregates.Total(MetricNames.YtdlpActive));
    }

    [Fact]
    public async Task Private_stream_urls_are_refused_without_starting_yt_dlp()
    {
        var track = new MusicTrackNew
        {
            Query = "q",
            Url = "http://127.0.0.1/good",
            Title = "t",
            Author = "a",
            Duration = null,
            ThumbnailUrl = null,
            Source = SongSource.Ytdlp
        };

        Assert.Null(await Resolver().GetStreamAsync(track));
        Assert.False(File.Exists(_argLog));
    }
}

[CollectionDefinition("PATH", DisableParallelization = true)]
public class PathCollection;
