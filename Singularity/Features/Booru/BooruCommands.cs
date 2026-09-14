using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Singularity.Features.Booru.Models;
using Singularity.Features.Booru.Services;

namespace Singularity.Features.Booru;

public class BooruCommands(BooruService booruService) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("e621", "Search for posts on e621 with optional filters.")]
    public async Task SearchE621Async(
        [SlashCommandParameter(Name = "tags", Description = "Space-separated search tags")]
        string tags = "",

        [SlashCommandParameter(Name = "rating", Description = "Override content rating filter")]
        BooruRating? rating = null,

        [SlashCommandParameter(Name = "type", Description = "Filter by media file format")]
        BooruMediaType? type = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        // forces safe rating for posts in SFW chats
        var isNsfwChannel = Context.Channel is TextGuildChannel guildChannel && guildChannel.Nsfw;
        if (!isNsfwChannel)
        {
            rating = BooruRating.Safe;
        }

        var embed = await booruService.GetRandomPostEmbedAsync(tags, rating, type);
        if (embed is null)
        {
            await ModifyResponseAsync(msg => msg.Content = "No posts were found.");
            return;
        }

        await ModifyResponseAsync(msg =>
        {
            msg.Content = string.Empty;
            msg.Embeds = [embed];
        });
    }
}
