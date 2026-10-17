using Singularity.Features.Music.Player;

namespace Singularity.Tests;

public class PlaybackPolicyTests
{
    [Theory]
    [InlineData(0, false, true)]  // nobody else listening
    [InlineData(1, false, true)]  // alone with the bot
    [InlineData(2, false, false)] // others present, no permission
    [InlineData(5, true, true)]   // moderator
    public void CanModerate(int listeners, bool moderator, bool expected) =>
        Assert.Equal(expected, PlaybackPolicy.CanModerate(listeners, moderator));

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void CanSkipImmediately(bool requester, bool moderator, bool expected) =>
        Assert.Equal(expected, PlaybackPolicy.CanSkipImmediately(requester, moderator));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(4, 3)]
    [InlineData(10, 6)]
    public void VotesNeeded_is_a_strict_majority(int listeners, int needed) =>
        Assert.Equal(needed, PlaybackPolicy.VotesNeeded(listeners));

    [Fact]
    public void VotePasses_at_the_threshold()
    {
        Assert.False(PlaybackPolicy.VotePasses(2, 4));
        Assert.True(PlaybackPolicy.VotePasses(3, 4));
    }
}
