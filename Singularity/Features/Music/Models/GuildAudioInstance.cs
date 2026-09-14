using Singularity.Features.Music.Services;
using NetCord.Services.ApplicationCommands;

namespace Singularity.Features.Music.Models;

internal class GuildAudioInstance(PlaybackHandler playbackHandler)
{
    internal PlaybackHandler PlaybackHandler => playbackHandler;

    internal void Initialize(ApplicationCommandContext context) => playbackHandler.SetContext(context);

    internal async Task<MusicTrackNew> EnqueueSongAsync(string term, bool next)
    {
        var song = await PlaybackHandler.AddSongAsync(term, next);

        if (!PlaybackHandler.Initialized) await PlaybackHandler.InitializeAsync();

        return song;
    }
}
