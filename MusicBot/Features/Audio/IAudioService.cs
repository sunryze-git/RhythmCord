using NetCord.Gateway.Voice;

namespace MusicBot.Features.Audio;

public interface IAudioService : IDisposable
{
    bool Looping { get; set; }
    TimeSpan Position { get; }

    Task StartAudioStreamAsync(Stream inStream, OpusEncodeStream outStream, CancellationToken stopToken, CancellationToken serviceToken = default);
}
