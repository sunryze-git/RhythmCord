using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetCord;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Services.ApplicationCommands;
using Singularity.Infrastructure;

namespace Singularity.Tests;

public class StartupWiringTests
{
    [Fact]
    public void The_music_commands_buttons_and_menus_register_on_a_real_host()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Discord:Token"] = "OTk5.AAAAAA.BBBBBBBBBBBBBBBBBBBBBBBBBBB" });
        builder.Services.AddDiscordGateway();
        builder.Services.AddApplicationCommands<ApplicationCommandInteraction, ApplicationCommandContext>();
        builder.Services.AddMusicInteractions();
        using var host = builder.Build();

        var registered = Record.Exception(() => host.AddMusicModules());

        Assert.Null(registered);
    }
}
