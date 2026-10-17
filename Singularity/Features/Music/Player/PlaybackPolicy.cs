namespace Singularity.Features.Music.Player;

/// <summary>Who may control playback. Pure rules, kept apart from Discord so they can be tested.</summary>
internal static class PlaybackPolicy
{
    /// <summary>Destructive commands (/stop, /leave): moderators, or whoever is alone with the bot.</summary>
    internal static bool CanModerate(int listeners, bool hasModeratorPermission) =>
        listeners <= 1 || hasModeratorPermission;

    /// <summary>The requester and moderators skip outright; everyone else needs a vote.</summary>
    internal static bool CanSkipImmediately(bool isRequester, bool canModerate) => isRequester || canModerate;

    /// <summary>Majority of the people listening.</summary>
    internal static int VotesNeeded(int listeners) => Math.Max(1, listeners / 2 + 1);

    internal static bool VotePasses(int votes, int listeners) => votes >= VotesNeeded(listeners);
}
