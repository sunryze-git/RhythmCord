using NetCord;
using NetCord.Gateway;
using NetCord.Services;
using Singularity.Features.Music.Player;

namespace Singularity.Features.Music.Models;

/// <summary>Who asked for something and where, whether it came from a slash command or from a button or menu.</summary>
internal sealed record RequestOrigin(GatewayClient Client, Guild? Guild, ulong UserId, ulong ChannelId, bool HasMoveMembers)
{
    internal static RequestOrigin From<TContext>(TContext context)
        where TContext : IGatewayClientContext, IGuildContext, IUserContext, IChannelContext, IInteractionContext
    {
        var hasMoveMembers = context.Interaction.User is GuildInteractionUser member &&
                             (member.Permissions.HasFlag(NetCord.Permissions.MoveUsers) ||
                              member.Permissions.HasFlag(NetCord.Permissions.Administrator));

        return new RequestOrigin(context.Client, context.Guild, context.User.Id, context.Channel?.Id ?? 0, hasMoveMembers);
    }

    /// <summary>Number of people (besides the bot) in the bot's voice channel.</summary>
    internal int ListenerCount()
    {
        if (Guild is null || !Guild.VoiceStates.TryGetValue(Client.Id, out var bot) || bot.ChannelId is not { } channelId)
            return 0;

        return Guild.VoiceStates.Count(kv => kv.Key != Client.Id && kv.Value.ChannelId == channelId);
    }

    internal bool CanModerate => PlaybackPolicy.CanModerate(ListenerCount(), HasMoveMembers);

    internal bool CanRemove(MusicTrackNew song) => song.RequestedBy == UserId || CanModerate;
}
