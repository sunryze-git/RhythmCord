using System.Text.Json.Serialization;

using MusicBot.Features.Media.Backends;

namespace MusicBot.Infrastructure;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SoundCloudTrack))]
[JsonSerializable(typeof(List<SoundCloudTrack>))]
[JsonSerializable(typeof(SoundCloudStreamInfo))]
[JsonSerializable(typeof(Song))]
[JsonSerializable(typeof(List<Song>))]
internal partial class SourceGenerationContext : JsonSerializerContext
{
}
