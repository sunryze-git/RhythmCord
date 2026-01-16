using Newtonsoft.Json;

namespace MusicBot.Records;

public record Config(
    [property: JsonProperty("token")] string Token
);
