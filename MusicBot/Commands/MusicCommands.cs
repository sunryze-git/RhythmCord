using Microsoft.Extensions.DependencyInjection;
using MusicBot.Exceptions;
using MusicBot.Services;
using MusicBot.Utilities;
using NetCord;
using NetCord.Gateway.Voice;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using YoutubeExplode.Videos;

namespace MusicBot.Commands;

public class MusicCommands : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("play", "Play a song by URL, or by a search query.")]
    public async Task Play(
        [SlashCommandParameter(Name = "query", Description = "URL or Search Query")] string query,
        [SlashCommandParameter(Name = "next", Description = "Insert as next in queue")] bool insertNext = false
    )
    {
        await RespondAsync(InteractionCallback.DeferredMessage());
        if (!UserInVoiceChannel())
        {
            await ModifyResponseAsync(message => message.Content = "You are not in a voice channel.");
            return;
        }
        
        try
        {
            var manager = GetManager();
            
            var song = await manager.PlaybackHandler.AddSongAsync(query, insertNext, Context);
            var embed = new EmbedProperties
            {
                Title = "Added to Queue",
                Description = $"**{song.Title}**\n**{song.Author}**",
                Thumbnail = new EmbedThumbnailProperties(song.Thumbnails[0].Url),
                Color = new Color(0, 0, 255)
            };

            await ModifyResponseAsync(message =>
            {
                message.Content = string.Empty;
                message.Embeds = [embed];
            });
        }
        catch (Exception e)
        {
            var response = $"""
                            An error occurred while adding the song:
                            ```{e.Message}```
                            """;
            await ModifyResponseAsync(message => message.Content = response);
        }
    }
    
    [SlashCommand("stop", "Stops song playback, clears queue.")]
    public async Task Stop()
    {
        if (!CheckVoiceChannelStatus())
        {
            await RespondAsync(InteractionCallback.Message("This command is not available."));
            return;
        }
        var manager = GetManager();
        if (manager.PlaybackHandler.Active)
        {
            manager.PlaybackHandler.Stop();
            await RespondAsync(InteractionCallback.Message("I have stopped the song and cleared the queue."));
        }
        else
        {
            await RespondAsync(InteractionCallback.Message("No song is currently playing."));
        }
    }
    
    [SlashCommand("skip", "Skips the current song.")]
    public async Task Skip()
    {
        if (!CheckVoiceChannelStatus())
        {
            await RespondAsync(InteractionCallback.Message("This command is not available."));
            return;
        }
        var manager = GetManager();
        
        if (manager.PlaybackHandler.Active)
        {
            manager.PlaybackHandler.SkipSong();
            await RespondAsync(InteractionCallback.Message("Song has been skipped."));
        }
        else
        {
            await RespondAsync(InteractionCallback.Message("No song is currently playing."));
        }
    }
    
    [SlashCommand("leave", "Leaves the VC.")]
    public async Task Leave()
    { 
        if (!CheckVoiceChannelStatus())
        {
            await RespondAsync(InteractionCallback.Message("This command is not available."));
            return;
        }
        await RespondAsync(InteractionCallback.Message("Bye! 👋"));
        var manager = GetManager();
        await manager.PlaybackHandler.End();
    }
    
    [SlashCommand("status", "Shows information about the current song.")]
    public async Task Status()
    {
        var ctx = Context;
        if (!CheckVoiceChannelStatus())
        {
            await RespondAsync(InteractionCallback.Message("This command is not available."));
            return;
        }
        var manager = GetManager();
        var playbackHandler = manager.PlaybackHandler;
        var song = playbackHandler.CurrentSong;

        if (song == null)
        {
            await RespondAsync(InteractionCallback.Message("No song is currently playing."));
            return;
        }
        
        var description = song switch
        {
            Video video => $"""
                            **{video.Title}**
                            **{video.Author}**

                            **Playback**: {playbackHandler.Position.ToAdaptivePlaybackString()} / {playbackHandler.Duration.ToAdaptivePlaybackString()}
                            **Upload Date**: {video.UploadDate}
                            **[Listen]({video.Url})**
                            """,
            _ => $"""
                  **{song.Title}**
                  **{song.Author}**

                  **Playback**: {playbackHandler.Position.ToAdaptivePlaybackString()} / {playbackHandler.Duration.ToAdaptivePlaybackString()}
                  **[Listen]({song.Url})**
                  """
        };

        var embed = new EmbedProperties
        {
            Title = "Current Song",
            Description = description,
            Thumbnail = new EmbedThumbnailProperties(song.Thumbnails[0].Url),
            Color = new Color(255, 255, 0),
            Footer = new EmbedFooterProperties
            {
                Text = $"Requested by {ctx.User.Username}"
            }
        };
        var properties = new InteractionMessageProperties
        {
            Embeds = [embed]
        };
        await RespondAsync(InteractionCallback.Message(properties));
    }
    
    [SlashCommand("loop", "Toggles looping the current playing song.")]
    public async Task Loop()
    {
        if (!CheckVoiceChannelStatus())
        {
            await RespondAsync(InteractionCallback.Message("This command is not available."));
            return;
        }
        var manager = GetManager();
        var song = manager.PlaybackHandler.CurrentSong;

        if (song == null)
        {
            await RespondAsync(InteractionCallback.Message("No song is currently playing."));
            return;
        }

        var loopStatus = manager.PlaybackHandler.ToggleLooping();
        await RespondAsync(InteractionCallback.Message(loopStatus
            ? $"Started looping {song.Title}"
            : $"Stopped looping {song.Title}"));
    }
    
    [SlashCommand("shuffle", "Shuffles the current queue.")]
    public async Task Shuffle()
    {
        if (!CheckVoiceChannelStatus())
        {
            await RespondAsync(InteractionCallback.Message("This command is not available."));
            return;
        }
        var manager = GetManager();
        if (manager.PlaybackHandler.CurrentSong == null)
        {
            await RespondAsync(InteractionCallback.Message("No song is currently playing."));
            return;
        }
        
        manager.PlaybackHandler.Shuffle();
        await RespondAsync(InteractionCallback.Message("Shuffled the queue."));
    }
    
    [SlashCommand("queue", "Shows the current queue.")]
    public async Task Queue()
    {
        if (!CheckVoiceChannelStatus())
        {
            await RespondAsync(InteractionCallback.Message("This command is not available."));
            return;
        }
        var manager = GetManager();
        var songs = manager.PlaybackHandler.SongQueue;

        EmbedProperties embed;
        if (songs.Count == 0)
        {
            embed = new EmbedProperties
            {
                Title = "Queue is empty",
                Description = "Use /play <query> to add songs to the queue!",
                Color = new Color(255, 0, 0)
            };
        }
        else
        {
            embed = new EmbedProperties
            {
                Title = "Next 10 songs",
                Footer = new EmbedFooterProperties
                {
                    Text = "Use /skip to skip the current song."
                },
                Color = new Color(0, 0, 255),
                Fields = songs
                    .Take(10)
                    .Select((i, index) => new EmbedFieldProperties
                    {
                        Inline = false,
                        Name = $"#{index+1} - {i.Title}",
                        Value = $"[Listen]({i.Url}) • {i.Duration}"
                    })
            };
        }

        var properties = new InteractionMessageProperties
        {
            Embeds = [embed]
        };
        await RespondAsync(InteractionCallback.Message(properties));
    }

    private GuildMusicService GetManager()
    {
        var globalService = MusicBot.Services!.GetRequiredService<GlobalMusicService>();
        return globalService.GetOrCreateManager(Context);
    }

    private bool UserInVoiceChannel() => 
        Context.Guild!.VoiceStates.TryGetValue(Context.User.Id, out var _);

    private bool SelfInVoiceChannel()
    {
        // we can assume if the guild has a music service, it is probably in use!
        var globalService = MusicBot.Services!.GetRequiredService<GlobalMusicService>();
        return globalService.GuildIsActive(Context.Guild!.Id);
    }

    private bool CheckVoiceChannelStatus()
    {
        return UserInVoiceChannel() && SelfInVoiceChannel();
    }
}