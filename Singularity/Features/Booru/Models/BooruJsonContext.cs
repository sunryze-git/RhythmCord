using System.Text.Json.Serialization;

namespace Singularity.Features.Booru.Models;

[JsonSerializable(typeof(E621Response))]
[JsonSerializable(typeof(E621Post))]
public partial class BooruJsonContext : JsonSerializerContext;
