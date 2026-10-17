namespace Singularity.Features.Booru;

/// <summary>Bound from the "Booru" configuration section.</summary>
public sealed class BooruOptions
{
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromSeconds(3);
}
