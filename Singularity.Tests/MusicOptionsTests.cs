using Microsoft.Extensions.Configuration;
using Singularity.Features.Music;

namespace Singularity.Tests;

public class MusicOptionsTests
{
    [Fact]
    public void Defaults_are_sensible()
    {
        var o = new MusicOptions();

        Assert.Equal(500, o.MaxQueueLength);
        Assert.Equal(TimeSpan.FromMinutes(10), o.IdleTimeout);
        Assert.True(o.Normalize);
        Assert.Empty(o.ResolverOrder);
        Assert.Equal(["youtube", "ytdlp"], o.EffectiveResolverOrder);
    }

    [Fact]
    public void Values_bind_from_configuration_including_timespans_and_arrays()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Music:MaxQueueLength"] = "42",
            ["Music:IdleTimeout"] = "00:03:00",
            ["Music:Normalize"] = "false",
            ["Music:ResolverOrder:0"] = "ytdlp",
            ["Music:ResolverOrder:1"] = "youtube"
        }).Build();

        var o = config.GetSection("Music").Get<MusicOptions>()!;

        Assert.Equal(42, o.MaxQueueLength);
        Assert.Equal(TimeSpan.FromMinutes(3), o.IdleTimeout);
        Assert.False(o.Normalize);
        Assert.Equal(["ytdlp", "youtube"], o.EffectiveResolverOrder);
        Assert.Equal(200, o.MaxPlaylistTracks); // untouched settings keep their defaults
    }
}
