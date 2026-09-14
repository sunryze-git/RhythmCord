using NetCord.Services.ApplicationCommands;

namespace Singularity.Features.Booru.Models;

public enum BooruRating
{
    [SlashCommandChoice(Name = "Safe")]
    Safe,
    [SlashCommandChoice(Name = "Questionable")]
    Questionable,
    [SlashCommandChoice(Name = "Explicit")]
    Explicit
}

public enum BooruMediaType
{
    [SlashCommandChoice(Name = "Photo")]
    Photo,

    [SlashCommandChoice(Name = "Gif")]
    Gif,
}
