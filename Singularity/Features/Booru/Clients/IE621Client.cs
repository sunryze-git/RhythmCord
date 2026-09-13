using Singularity.Features.Booru.Models;

namespace Singularity.Features.Booru.Clients;

public interface IE621Client
{
    /// <summary>
    /// Searches e621 for posts matching the specified space-separated tags.
    /// </summary>
    Task<List<E621Post>> GetPostsAsync(string tags, int limit = 10, CancellationToken cancellationToken = default);
}
