using Newtonsoft.Json;

namespace MusicBot.Parsers;

public record Config(
    [property: JsonProperty("token")] string Token
);
