namespace MusicBot.Configuration;

public record BotConfig
{
    public string Token { get; init; } = null!;
    public string CobaltUrl { get; init; } = null!;
    public ResolverSettings Resolvers { get; init; } = null!;
}

public record ResolverSettings
{
    public bool EnableCobalt { get; set; }
    public bool EnableDirect { get; set; }
    public bool EnableSoundCloud { get; set; }
    public bool EnableYouTube { get; set; }
    public bool EnableYtdlp { get; set; }
}
