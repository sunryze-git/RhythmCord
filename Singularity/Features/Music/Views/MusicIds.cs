namespace Singularity.Features.Music.Views;

/// <summary>
///     Custom IDs of the buttons and menus. The part before the first ':' picks the handler in
///     <see cref="MusicButtons" /> or <see cref="MusicMenus" />; whatever follows is passed to it as parameters.
/// </summary>
internal static class MusicIds
{
    internal const string Skip = "music-skip";
    internal const string Loop = "music-loop";
    internal const string Shuffle = "music-shuffle";
    internal const string Stop = "music-stop";
    internal const string Queue = "music-queue";
    internal const string Refresh = "music-refresh";
    internal const string Page = "music-page";
    internal const string Remove = "music-remove";
    internal const string Search = "music-search";

    internal static string PageOf(int page) => $"{Page}:{page}";
    internal static string RemoveFrom(int page) => $"{Remove}:{page}";
    internal static string SearchFor(string token) => $"{Search}:{token}";
}
