using Singularity.Infrastructure;

namespace Singularity.Features.Music.Resolvers;

/// <summary>The request itself was the problem (nothing found, URL not allowed, queue full...).</summary>
internal class SearchException(string message) : UserFacingException(message);
