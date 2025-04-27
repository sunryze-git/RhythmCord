using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace MusicBot.Commands;

public class BasicCommands : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("test", "Test Command.")]
    public async Task Test(
        [SlashCommandParameter(Name = "number", Description = "What can I say?")] int number
    )
    {
        InteractionCallback callback = InteractionCallback.Message($"Test Command: {number}");
        await RespondAsync(callback);
    }

    [SlashCommand("embed", "Test Embed Command.")]
    public async Task Embed()
    {
        var embedProperties = new EmbedProperties
        {
            Title = "Embed Title",
            Color = new Color(255, 0, 0),
            Footer = new EmbedFooterProperties
            {
                Text = "Embed Footer",
            },
            Description = "This is a test embed description.",
            Thumbnail = new EmbedThumbnailProperties("https://media.istockphoto.com/id/187722063/photo/funny-man-with-watermelon-helmet-and-goggles.jpg?s=612x612&w=0&k=20&c=gRAm8vtLqdOU8a-mJVt6m_Wnv8pLpa3TBh2vRQP4208=")
        };
        var message = new InteractionMessageProperties()
        {
            Content = "This is a test embed message.",
            Embeds = [embedProperties]
        };
        var callback = InteractionCallback.Message(message);
            
        await RespondAsync(callback);
    }

    [SlashCommand("defer", "Command that takes a while.")]
    public async Task Defer()
    {
        var callback = InteractionCallback.DeferredMessage();
        await RespondAsync(callback);
            
        // do stuff here
        await Task.Delay(TimeSpan.FromSeconds(5));
            
        await ModifyResponseAsync(message => message.Content = "Hello 5 seconds later!");
    }
}