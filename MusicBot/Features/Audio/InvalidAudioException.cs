namespace MusicBot.Features.Audio;

public class InvalidAudioException(string message, string audioFormat) : Exception(message)
{
    public string AudioFormat { get; } = audioFormat;
}
