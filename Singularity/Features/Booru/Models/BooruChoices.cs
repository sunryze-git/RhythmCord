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
    [SlashCommandChoice(Name = "Photo (Still Image)")]
    Photo,

    [SlashCommandChoice(Name = "Gif (Animated Image)")]
    Gif,

    [SlashCommandChoice(Name = "Video (MP4 / WebM)")]
    Video
}
