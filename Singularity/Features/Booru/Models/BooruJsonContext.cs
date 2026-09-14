using System.Text.Json.Serialization;

namespace Singularity.Features.Booru.Models;

[JsonSerializable(typeof(E621Response))]
[JsonSerializable(typeof(E621Post))]
internal partial class BooruJsonContext : JsonSerializerContext;
