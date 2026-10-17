using Singularity.Features.Music.Models;
using Singularity.Features.Music.Permissions;
using Singularity.Features.Music.Player;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Singularity.Infrastructure;
using Singularity.Observability;
using Microsoft.Extensions.Logging;

namespace Singularity.Features.Music;

public class MusicCommands(
    ILogger<MusicCommands> logger,
    GuildAudioInstanceOrchestrator orchestrator,
    CommandCooldown cooldown,
    MusicOptions options,
    BotMetrics metrics,
    ErrorReporter errors) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("play", "Play a song by URL, or by a search query.")]
    [RequireBotConnectPermission]
    [RequireSameVoiceChannelIfConnected]
    public async Task PlayAsync(
        [SlashCommandParameter(Name = "query", Description = "URL or Search Query")] string query,
        [SlashCommandParameter(Name = "next", Description = "Insert as next in queue")] bool insertNext = false)
    {
        using var scope = Begin("play");

        if (!cooldown.TryAcquire("play", Context.User.Id, options.PlayCooldown, out var remaining))
        {
            metrics.CooldownHit();
            await RespondTextAsync($"Slow down! Try again in {Math.Ceiling(remaining.TotalSeconds)}s.", ephemeral: true);
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage());

        GuildAudioInstance? manager = null;
        try
        {
            manager = GetManager();
            var songs = await manager.EnqueueAsync(Context, query, insertNext);
            var song = songs[0];

            var description = songs.Count > 1
                ? $"**{song.Title}**\n*{song.Author}*\n\n…and {songs.Count - 1} more track(s)"
                : $"**{song.Title}**\n*{song.Author}*";

            var embed = new EmbedProperties
            {
                Title = songs.Count > 1 ? "Added Playlist to Queue" : "Added to Queue",
                Description = description,
                Thumbnail = song.ThumbnailUrl is not null ? new EmbedThumbnailProperties(song.ThumbnailUrl) : null,
                Color = new Color(0, 122, 255)
            };

            await TryModifyResponseAsync(message =>
            {
                message.Content = string.Empty;
                message.Embeds = [embed];
            });
        }
        catch (Exception e)
        {
            // A session that never managed to join voice would otherwise linger as an empty shell.
            if (manager is { Voice.IsJoined: false } && Context.Guild is { } guild)
                await orchestrator.CloseManagerAsync(guild.Id);

            // Expected problems (nothing found, join failed, queue full...) show their own message; bugs show a generic one.
            var message = errors.Report(e, "play");
            await TryModifyResponseAsync(response => response.Content = message);
        }
    }

    [SlashCommand("stop", "Stops song playback, clears queue.")]
    [RequireUserVoice]
    [RequireBotInVoiceChannel]
    [RequireSameVoiceChannel]
    public async Task StopAsync()
    {
        using var scope = Begin("stop");

        if (!CanModerate())
        {
            await RespondNoPermissionAsync();
            return;
        }

        var player = GetManager().Player;
        if (!player.Snapshot.HasWork)
        {
            await RespondNoSongPlayingAsync();
            return;
        }

        await player.StopAsync();
        await RespondTextAsync("Stopped playback and cleared the queue.");
    }

    [SlashCommand("skip", "Skips the current song (majority vote if others are listening).")]
    [RequireUserVoice]
    [RequireBotInVoiceChannel]
    [RequireSameVoiceChannel]
    public async Task SkipAsync()
    {
        using var scope = Begin("skip");

        var player = GetManager().Player;
        var song = player.Snapshot.Current;
        if (song is null)
        {
            await RespondNoSongPlayingAsync();
            return;
        }

        // The requester, a moderator, or the only listener can skip outright; everyone else votes.
        if (PlaybackPolicy.CanSkipImmediately(song.RequestedBy == Context.User.Id, CanModerate()))
        {
            await player.SkipAsync(song);
            await RespondTextAsync("Skipped the current song.");
            return;
        }

        var listeners = ListenerCount();
        var needed = PlaybackPolicy.VotesNeeded(listeners);
        var votes = await player.VoteSkipAsync(Context.User.Id, song);

        if (votes < 0)
            await RespondTextAsync("That song already finished.");
        else if (PlaybackPolicy.VotePasses(votes, listeners))
        {
            await player.SkipAsync(song);
            await RespondTextAsync($"Vote passed ({votes}/{needed}). Skipped the current song.");
        }
        else
            await RespondTextAsync($"Skip vote registered ({votes}/{needed}).");
    }

    [SlashCommand("leave", "Leaves the voice channel.")]
    [RequireUserVoice]
    [RequireBotInVoiceChannel]
    [RequireSameVoiceChannel]
    public async Task LeaveAsync()
    {
        using var scope = Begin("leave");

        if (!CanModerate())
        {
            await RespondNoPermissionAsync();
            return;
        }

        await RespondTextAsync("Bye! 👋");

        if (Context.Guild != null)
        {
            await orchestrator.CloseManagerAsync(Context.Guild.Id);
        }
    }

    [SlashCommand("status", "Shows information about the current song.")]
    [RequireUserVoice]
    [RequireBotInVoiceChannel]
    [RequireSameVoiceChannel]
    public async Task StatusAsync()
    {
        using var scope = Begin("status");

        var player = GetManager().Player;
        var song = player.Snapshot.Current;

        if (song is null)
        {
            await RespondNoSongPlayingAsync();
            return;
        }

        var duration = song.Duration ?? TimeSpan.Zero;
        var description = $"""
            **{song.Title}**
            *{song.Author}*

            **Playback**: {player.Position.ToAdaptivePlaybackString()} / {duration.ToAdaptivePlaybackString()}
            **[Listen on Source]({song.Url})**
            """;

        var embed = new EmbedProperties
        {
            Title = "Now Playing",
            Description = description,
            Thumbnail = song.ThumbnailUrl is not null ? new EmbedThumbnailProperties(song.ThumbnailUrl) : null,
            Color = new Color(255, 204, 0),
        };

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties { Embeds = [embed] }));
    }

    [SlashCommand("loop", "Toggles looping the current playing song.")]
    [RequireUserVoice]
    [RequireBotInVoiceChannel]
    [RequireSameVoiceChannel]
    public async Task LoopAsync()
    {
        using var scope = Begin("loop");

        var player = GetManager().Player;
        var song = player.Snapshot.Current;

        if (song is null)
        {
            await RespondNoSongPlayingAsync();
            return;
        }

        var isLooping = await player.ToggleLoopAsync(song);
        await RespondTextAsync(isLooping
            ? $"🔁 Started looping **{song.Title}**"
            : $"➡️ Stopped looping **{song.Title}**");
    }

    [SlashCommand("shuffle", "Shuffles the current queue.")]
    [RequireUserVoice]
    [RequireBotInVoiceChannel]
    [RequireSameVoiceChannel]
    public async Task ShuffleAsync()
    {
        using var scope = Begin("shuffle");

        var player = GetManager().Player;
        if (!player.Snapshot.HasWork)
        {
            await RespondNoSongPlayingAsync();
            return;
        }

        await player.ShuffleAsync();
        await RespondTextAsync("🔀 Queue shuffled.");
    }

    [SlashCommand("queue", "Shows the current queue.")]
    [RequireUserVoice]
    [RequireBotInVoiceChannel]
    [RequireSameVoiceChannel]
    public async Task QueueAsync(
        [SlashCommandParameter(Name = "page", Description = "Page number")] int page = 1)
    {
        using var scope = Begin("queue");

        var snapshot = GetManager().Player.Snapshot;
        var songs = snapshot.Pending;
        var current = snapshot.Current;
        var nowPlaying = current is null ? null : $"Now playing: **{current.Title}**";
        var pageSize = Math.Max(1, options.QueuePageSize);

        EmbedProperties embed;
        if (songs.Count == 0)
        {
            embed = new EmbedProperties
            {
                Title = "Queue is empty",
                Description = nowPlaying is null
                    ? "Use `/play <query>` to add songs to the queue!"
                    : $"{nowPlaying}\nUse `/play <query>` to add more songs!",
                Color = new Color(255, 59, 48)
            };
        }
        else
        {
            var pageCount = (songs.Count + pageSize - 1) / pageSize;
            page = Math.Clamp(page, 1, pageCount);
            var offset = (page - 1) * pageSize;

            embed = new EmbedProperties
            {
                Title = $"Up Next — page {page}/{pageCount}",
                Description = nowPlaying,
                Color = new Color(0, 122, 255),
                Footer = new EmbedFooterProperties
                {
                    Text = $"{songs.Count} track(s) queued • /queue page:<n> for more • /skip for the next track"
                },
                Fields = songs.Skip(offset).Take(pageSize).Select((song, index) => new EmbedFieldProperties
                {
                    Inline = false,
                    Name = $"#{offset + index + 1} - {Truncate(song.Title, 200)}",
                    Value = $"[Listen]({song.Url}) • {song.Duration?.ToAdaptivePlaybackString() ?? "Live Stream"}"
                })
            };
        }

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties { Embeds = [embed] }));
    }

    #region Helpers

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    // Counts the command (by name only) and tags this call's logs with guild/user context (console only).
    private IDisposable? Begin(string command)
    {
        metrics.Command(command);
        return logger.BeginScope("guild={GuildId} user={UserId}", Context.Guild?.Id, Context.User.Id);
    }

    private GuildAudioInstance GetManager() => orchestrator.GetOrCreateManager(Context.Guild!.Id);

    // Number of people (besides the bot) in the bot's voice channel.
    private int ListenerCount()
    {
        var guild = Context.Guild!;
        if (!guild.VoiceStates.TryGetValue(Context.Client.Id, out var bot) || bot.ChannelId is not { } channelId)
            return 0;

        return guild.VoiceStates.Count(kv => kv.Key != Context.Client.Id && kv.Value.ChannelId == channelId);
    }

    private bool CanModerate()
    {
        var isModerator = Context.User is GuildInteractionUser member &&
                          (member.Permissions.HasFlag(NetCord.Permissions.MoveUsers) ||
                           member.Permissions.HasFlag(NetCord.Permissions.Administrator));

        return PlaybackPolicy.CanModerate(ListenerCount(), isModerator);
    }

    private Task RespondNoSongPlayingAsync() => RespondTextAsync("No song is currently playing.");

    private Task RespondNoPermissionAsync() =>
        RespondTextAsync("You need the Move Members permission (or be alone with me) to do that.", ephemeral: true);

    // Messages can contain track titles, so they never get to ping anyone.
    private Task RespondTextAsync(string text, bool ephemeral = false) =>
        RespondAsync(InteractionCallback.Message(new InteractionMessageProperties
        {
            Content = text,
            AllowedMentions = AllowedMentionsProperties.None,
            Flags = ephemeral ? MessageFlags.Ephemeral : null
        }));

    private async Task TryModifyResponseAsync(Action<MessageOptions> action)
    {
        try
        {
            await ModifyResponseAsync(action);
        }
        catch (Exception e)
        {
            errors.Record(e, "play.respond", "Could not deliver the /play result to Discord.");
        }
    }

    #endregion
}
