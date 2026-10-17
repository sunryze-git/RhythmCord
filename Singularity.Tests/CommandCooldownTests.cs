using Singularity.Infrastructure;

namespace Singularity.Tests;

public class CommandCooldownTests
{
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(250);

    [Fact]
    public void First_use_is_allowed_and_an_immediate_repeat_is_not()
    {
        var cd = new CommandCooldown();

        Assert.True(cd.TryAcquire("play", 1, Window, out _));
        Assert.False(cd.TryAcquire("play", 1, Window, out var remaining));
        Assert.InRange(remaining, TimeSpan.Zero, Window);
    }

    [Fact]
    public void Cooldowns_are_per_user_and_per_command()
    {
        var cd = new CommandCooldown();
        cd.TryAcquire("play", 1, Window, out _);

        Assert.True(cd.TryAcquire("play", 2, Window, out _));
        Assert.True(cd.TryAcquire("e621", 1, Window, out _));
    }

    [Fact]
    public async Task Allowed_again_after_the_window()
    {
        var cd = new CommandCooldown();
        cd.TryAcquire("play", 1, Window, out _);

        await Task.Delay(Window + TimeSpan.FromMilliseconds(80));

        Assert.True(cd.TryAcquire("play", 1, Window, out _));
    }
}
