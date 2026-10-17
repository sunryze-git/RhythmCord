using System.Net;
using Singularity.Features.Music;
using YoutubeExplode.Exceptions;

namespace Singularity.Infrastructure;

/// <summary>Turns an exception from the media layer into a short reason a user can understand and act on.</summary>
internal static class FailureDescriber
{
    internal static string Describe(Exception exception)
    {
        return exception switch
        {
            UserFacingException user => user.Message,
            ResolverFailedException resolver => resolver.Message,
            VideoRequiresPurchaseException => "the video requires a purchase",
            VideoUnavailableException => "the video is unavailable (private, removed, age-restricted or blocked where the bot runs)",
            RequestLimitExceededException => "YouTube is rate-limiting the bot right now",
            HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => "the source is rate-limiting the bot right now",
            HttpRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized } => "the source refused the request",
            HttpRequestException { StatusCode: HttpStatusCode.NotFound or HttpStatusCode.Gone } => "the source no longer has this media",
            HttpRequestException => "a network error occurred",
            TaskCanceledException or TimeoutException => "the request timed out",
            _ => "an unexpected error occurred"
        };
    }
}
