using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Singularity.Features.Music;
using Singularity.Features.Music.Models;
using Singularity.Features.Music.Player;
using Singularity.Features.Music.Resolvers;
using Singularity.Features.Music.Services;
using Singularity.Infrastructure;
using Singularity.Observability;

namespace Singularity.Tests;

/// <summary>Builds the real music service graph (validated) so wiring mistakes fail here instead of at 2am in production.</summary>
public sealed class DependencyInjectionTests : IAsyncLifetime
{
    private ServiceProvider _provider = null!;
    private GuildAudioInstanceOrchestrator _orchestrator = null!;

    public Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new MusicOptions { IdleTimeout = TimeSpan.Zero, AloneCheckInterval = TimeSpan.Zero });
        services.AddSingleton(new BotMetrics());
        services.AddMusicFeature();

        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        _orchestrator = _provider.GetRequiredService<GuildAudioInstanceOrchestrator>();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _orchestrator.CloseAllManagersAsync();
        await _provider.DisposeAsync();
    }

    [Fact]
    public void Resolvers_are_registered_and_ordered_by_the_default_configuration()
    {
        var media = _provider.GetRequiredService<MediaResolver>();

        Assert.Equal(["youtube", "ytdlp"], media.Resolvers.Select(r => r.Name));
        Assert.Same(media, _provider.GetRequiredService<ITrackSource>());
    }

    [Fact]
    public void Each_guild_gets_its_own_player_and_voice_session_and_the_same_one_every_time()
    {
        var first = _orchestrator.GetOrCreateManager(1);
        var again = _orchestrator.GetOrCreateManager(1);
        var other = _orchestrator.GetOrCreateManager(2);

        Assert.Same(first, again);
        Assert.NotSame(first.Player, other.Player);
        Assert.NotSame(first.Voice, other.Voice);
        Assert.False(first.Voice.IsJoined);
        Assert.Equal(PlayerSnapshot.Empty.Closing, first.Player.Snapshot.Closing);
    }

    [Fact]
    public void Session_totals_cover_every_guild_without_naming_any()
    {
        _orchestrator.GetOrCreateManager(1);
        _orchestrator.GetOrCreateManager(2);

        var totals = _orchestrator.GetAggregate();

        Assert.Equal(2, totals.Sessions);
        Assert.Equal(0, totals.Playing);
    }

    [Fact]
    public async Task Closing_a_manager_removes_it_and_a_new_one_can_be_created()
    {
        var first = _orchestrator.GetOrCreateManager(1);

        await _orchestrator.CloseManagerAsync(1);

        Assert.False(_orchestrator.GuildIsActive(1));
        Assert.NotSame(first, _orchestrator.GetOrCreateManager(1));
    }

    [Fact]
    public async Task A_player_that_asks_to_close_gets_its_session_removed()
    {
        var instance = _orchestrator.GetOrCreateManager(1);

        instance.Player.RequestClose();

        for (var i = 0; i < 300 && _orchestrator.GuildIsActive(1); i++) await Task.Delay(10);
        Assert.False(_orchestrator.GuildIsActive(1));
    }
}
