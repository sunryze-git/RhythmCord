using System.Text.Json.Serialization;

namespace Singularity.Features.Booru.Models;

internal record E621Response(
    [property: JsonPropertyName("posts")] List<E621Post> Posts
);
