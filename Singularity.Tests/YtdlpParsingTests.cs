using Singularity.Features.Music.Models;
using Singularity.Features.Music.Resolvers;

namespace Singularity.Tests;

public class YtdlpParsingTests
{
    [Fact]
    public void Single_video_payload_maps_to_one_track()
    {
        var json = """
            {"title":"Song","webpage_url":"https://x/watch?v=1","uploader":"Artist","duration":61.5,"thumbnail":"https://t/1.jpg","id":"1"}
            """;

        var track = Assert.Single(YtdlpResolver.ParseYtdlpJson(json, "query"));

        Assert.Equal("Song", track.Title);
        Assert.Equal("https://x/watch?v=1", track.Url);
        Assert.Equal("Artist", track.Author);
        Assert.Equal(TimeSpan.FromSeconds(61.5), track.Duration);
        Assert.Equal("1", track.MediaId);
        Assert.Equal(SongSource.Ytdlp, track.Source);
        Assert.Equal("query", track.Query);
    }

    [Fact]
    public void Playlist_payload_maps_each_entry_and_skips_untitled_ones()
    {
        var json = """
            {"entries":[{"title":"A","url":"https://x/a"},{"url":"https://x/untitled"},{"title":"B","url":"https://x/b","channel":"Chan"}]}
            """;

        var tracks = YtdlpResolver.ParseYtdlpJson(json, "q");

        Assert.Equal(["A", "B"], tracks.Select(t => t.Title));
        Assert.Equal("Chan", tracks[1].Author);
    }

    [Fact]
    public void Missing_or_odd_fields_get_sensible_defaults()
    {
        var json = """{"title":"T","duration":null,"uploader":42}""";

        var track = Assert.Single(YtdlpResolver.ParseYtdlpJson(json, "https://fallback/url"));

        Assert.Equal("Unknown Artist", track.Author);
        Assert.Null(track.Duration);
        Assert.Equal("https://fallback/url", track.Url);
    }
}
