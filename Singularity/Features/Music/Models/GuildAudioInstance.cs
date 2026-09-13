using Singularity.Features.Music.Services;
using NetCord.Services.ApplicationCommands;

namespace Singularity.Features.Music.Models;

public class GuildAudioInstance(PlaybackHandler playbackHandler)
{
    public PlaybackHandler PlaybackHandler => playbackHandler;

    public void Initialize(ApplicationCommandContext context) => playbackHandler.SetContext(context);

    public async Task<MusicTrackNew> EnqueueSongAsync(string term, bool next)
    {
        var song = await PlaybackHandler.AddSongAsync(term, next);

        if (!PlaybackHandler.Initialized) await PlaybackHandler.InitializeAsync();

        return song;
    }
}
