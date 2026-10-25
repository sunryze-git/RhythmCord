namespace Singularity.Features.Music.Player;

public enum LoopMode
{
    Off,
    Track,
    Queue
}

internal static class LoopModeExtensions
{
    internal static LoopMode Next(this LoopMode mode) => mode switch
    {
        LoopMode.Off => LoopMode.Track,
        LoopMode.Track => LoopMode.Queue,
        _ => LoopMode.Off
    };
}
