using MusicBot.Features.Audio;
using MusicBot.Infrastructure;

using NetCord.Services.ApplicationCommands;

namespace MusicBot.Features;

public class GuildAudioInstance(
    AudioServiceNative audioService,
    PlaybackHandler playbackHandler) : IDisposable
{
    public PlaybackHandler PlaybackHandler => playbackHandler;

    public void Dispose()
    {
        playbackHandler.Dispose();
        audioService.Dispose();
    }

    public void Initialize(ApplicationCommandContext context) => playbackHandler.SetContext(context);

    public async Task<MusicTrack> EnqueueSongAsync(string term, bool next)
    {
        var song = await PlaybackHandler.AddSongAsync(term, next);

        if (!PlaybackHandler.Initialized) await PlaybackHandler.InitializeAsync();

        return song;
    }
}
