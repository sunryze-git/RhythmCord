using NetCord.Gateway;

namespace Singularity.Features.Music.Permissions;

/// <summary>Voice channel rules shared by slash command preconditions and button or menu handlers. Null means allowed.</summary>
internal static class VoiceChecks
{
    internal static string? SameChannel(Guild? guild, ulong userId, ulong botId)
    {
        if (guild is null)
            return "This command can only be used in a server.";

        if (!guild.VoiceStates.TryGetValue(userId, out var userState) || userState.ChannelId == null)
            return "You are not in a voice channel.";

        if (!guild.VoiceStates.TryGetValue(botId, out var botState) || botState.ChannelId == null)
            return "I am not in a voice channel.";

        return userState.ChannelId == botState.ChannelId ? null : "You must be in the same voice channel as me!";
    }

    // Like SameChannel, but passes when the bot is not in a voice channel yet (e.g. the first /play).
    internal static string? SameChannelIfConnected(Guild? guild, ulong userId, ulong botId)
    {
        if (guild is null)
            return "This command can only be used in a server.";

        if (!guild.VoiceStates.TryGetValue(botId, out var botState) || botState.ChannelId == null)
            return null;

        return guild.VoiceStates.TryGetValue(userId, out var userState) && userState.ChannelId == botState.ChannelId
            ? null
            : "You must be in the same voice channel as me!";
    }
}
