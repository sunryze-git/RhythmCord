using Microsoft.Extensions.Options;
using Singularity.Features.Music.Models;

namespace Singularity.Tests;

internal static class Tracks
{
    internal static MusicTrackNew Make(string title, SongSource source = SongSource.Ytdlp, string? mediaId = null) => new()
    {
        Query = title,
        Url = "https://example.com/" + Uri.EscapeDataString(title),
        Title = title,
        Author = "author",
        Duration = TimeSpan.FromSeconds(180),
        ThumbnailUrl = null,
        Source = source,
        MediaId = mediaId
    };

    internal static List<MusicTrackNew> Many(string prefix, int count) =>
        Enumerable.Range(0, count).Select(i => Make($"{prefix}{i}")).ToList();
}

internal sealed class StaticMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
