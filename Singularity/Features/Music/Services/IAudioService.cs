using NetCord.Gateway.Voice;

namespace Singularity.Features.Music.Services;

public interface IAudioService : IAsyncDisposable
{
    bool Looping { get; set; }
    TimeSpan Position { get; }

    Task StartAudioStreamAsync(Stream inStream, OpusEncodeStream outStream, CancellationToken stopToken, CancellationToken serviceToken = default);
}
