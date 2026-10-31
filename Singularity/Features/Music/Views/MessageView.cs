using NetCord;
using NetCord.Rest;

namespace Singularity.Features.Music.Views;

/// <summary>A Components V2 message, ready to be sent as a new message or to replace an existing one.</summary>
internal sealed record MessageView(IReadOnlyList<IMessageComponentProperties> Components)
{
    internal InteractionMessageProperties ToMessage(bool ephemeral = false) => new()
    {
        Components = Components,
        Flags = ephemeral ? MessageFlags.IsComponentsV2 | MessageFlags.Ephemeral : MessageFlags.IsComponentsV2,
        AllowedMentions = AllowedMentionsProperties.None
    };

    internal MessageProperties ToChannelMessage() => new()
    {
        Components = Components,
        Flags = MessageFlags.IsComponentsV2,
        AllowedMentions = AllowedMentionsProperties.None
    };

    internal void ApplyTo(MessageOptions options)
    {
        options.Components = Components;
        options.Flags = MessageFlags.IsComponentsV2;
        options.AllowedMentions = AllowedMentionsProperties.None;
    }
}
