using MusicBot.Features.Music.Models;
using MusicBot.Features.Music.Permissions;
using MusicBot.Infrastructure;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace MusicBot.Features.Music;

public class MusicCommands(GuildAudioInstanceOrchestrator orchestrator) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("play", "Play a song by URL, or by a search query.")]
    [RequireBotConnectPermission]
    public async Task PlayAsync(
        [SlashCommandParameter(Name = "query", Description = "URL or Search Query")] string query,
        [SlashCommandParameter(Name = "next", Description = "Insert as next in queue")] bool insertNext = false)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        try
        {
            var manager = GetManager();
            var song = await manager.EnqueueSongAsync(query, insertNext);

            manager.PlaybackHandler.StartQueue();

            var embed = new EmbedProperties
            {
                Title = "Added to Queue",
                Description = $"**{song.Title}**\n*{song.Author}*",
                Thumbnail = song.ThumbnailUrl is not null ? new EmbedThumbnailProperties(song.ThumbnailUrl) : null,
                Color = new Color(0, 122, 255)
            };

            await ModifyResponseAsync(message =>
            {
                message.Content = string.Empty;
                message.Embeds = [embed];
            });
        }
        catch (Exception e)
        {
            await ModifyResponseAsync(message => message.Content = $"An error occurred while adding the song:\n```{e.Message}```");
        }
    }

    [SlashCommand("stop", "Stops song playback, clears queue.")]
    [RequireUserVoice]
    [RequireBotInVoiceChannel]
    [RequireSameVoiceChannel]
    public async Task StopAsync()
    {
        var manager = GetManager();
        if (!manager.PlaybackHandler.Active)
        {
            await RespondNoSongPlayingAsync();
            return;
        }

        manager.PlaybackHandler.Stop();
        await RespondAsync(InteractionCallback.Message("Stopped playback and cleared the queue."));
    }

    [SlashCommand("skip", "Skips the current song.")]
    [RequireUserVoice]
    [RequireBotInVoiceChannel]
    [RequireSameVoiceChannel]
    public async Task SkipAsync()
    {
        var manager = GetManager();
        if (!manager.PlaybackHandler.Active)
        {
            await RespondNoSongPlayingAsync();
            return;
        }

        manager.PlaybackHandler.SkipSong();
        await RespondAsync(InteractionCallback.Message("Skipped the current song."));
    }

    [SlashCommand("leave", "Leaves the voice channel.")]
    [RequireUserVoice]
    [RequireBotInVoiceChannel]
    [RequireSameVoiceChannel]
    public async Task LeaveAsync()
    {
        await RespondAsync(InteractionCallback.Message("Bye! 👋"));

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
        var manager = GetManager();
        var playback = manager.PlaybackHandler;
        var song = playback.CurrentSong;

        if (song is null)
        {
            await RespondNoSongPlayingAsync();
            return;
        }

        var description = $"""
            **{song.Title}**
            *{song.Author}*

            **Playback**: {playback.Position.ToAdaptivePlaybackString()} / {playback.Duration.ToAdaptivePlaybackString()}
            **[Listen on Source]({song.Url})**
            """;

        var embed = new EmbedProperties
        {
            Title = "Now Playing",
            Description = description,
            Thumbnail = song.ThumbnailUrl is not null ? new EmbedThumbnailProperties(song.ThumbnailUrl) : null,
            Color = new Color(255, 204, 0),
            Footer = new EmbedFooterProperties { Text = $"Requested by {Context.User.Username}" }
        };

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties { Embeds = [embed] }));
    }

    [SlashCommand("loop", "Toggles looping the current playing song.")]
    [RequireUserVoice]
    [RequireBotInVoiceChannel]
    [RequireSameVoiceChannel]
    public async Task LoopAsync()
    {
        var manager = GetManager();
        var song = manager.PlaybackHandler.CurrentSong;

        if (song is null)
        {
            await RespondNoSongPlayingAsync();
            return;
        }

        var isLooping = manager.PlaybackHandler.ToggleLooping();
        await RespondAsync(InteractionCallback.Message(isLooping
            ? $"🔁 Started looping **{song.Title}**"
            : $"➡️ Stopped looping **{song.Title}**"));
    }

    [SlashCommand("shuffle", "Shuffles the current queue.")]
    [RequireUserVoice]
    [RequireBotInVoiceChannel]
    [RequireSameVoiceChannel]
    public async Task ShuffleAsync()
    {
        var manager = GetManager();
        if (!manager.PlaybackHandler.Active)
        {
            await RespondNoSongPlayingAsync();
            return;
        }

        manager.PlaybackHandler.Shuffle();
        await RespondAsync(InteractionCallback.Message("🔀 Queue shuffled."));
    }

    [SlashCommand("queue", "Shows the current queue.")]
    [RequireUserVoice]
    [RequireBotInVoiceChannel]
    [RequireSameVoiceChannel]
    public async Task QueueAsync()
    {
        var manager = GetManager();
        var songs = manager.PlaybackHandler.SongQueue;

        EmbedProperties embed = songs.Count == 0
            ? new EmbedProperties
            {
                Title = "Queue is empty",
                Description = "Use `/play <query>` to add songs to the queue!",
                Color = new Color(255, 59, 48)
            }
            : new EmbedProperties
            {
                Title = "Up Next (Top 10)",
                Color = new Color(0, 122, 255),
                Footer = new EmbedFooterProperties { Text = "Use /skip to move to the next track." },
                Fields = songs.Take(10).Select((song, index) => new EmbedFieldProperties
                {
                    Inline = false,
                    Name = $"#{index + 1} - {song.Title}",
                    Value = $"[Listen]({song.Url}) • {song.Duration?.ToAdaptivePlaybackString() ?? "Live Stream"}"
                })
            };

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties { Embeds = [embed] }));
    }

    #region Helpers

    private GuildAudioInstance GetManager()
    {
        var manager = orchestrator.GetOrCreateManager(Context);
        manager.PlaybackHandler.SetContext(Context);
        return manager;
    }

    private Task RespondNoSongPlayingAsync() =>
        RespondAsync(InteractionCallback.Message("No song is currently playing."));

    #endregion
}
