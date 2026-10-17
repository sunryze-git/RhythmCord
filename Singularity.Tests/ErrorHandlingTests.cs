using System.Net;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetCord.Services;
using Singularity.Features.Booru;
using Singularity.Features.Booru.Clients;
using Singularity.Features.Music;
using Singularity.Features.Music.Player;
using Singularity.Features.Music.Resolvers;
using Singularity.Infrastructure;
using Singularity.Observability;

namespace Singularity.Tests;

public class ErrorHandlingTests
{
    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception));
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public CapturingLogger Inner { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Inner.Log(logLevel, eventId, state, exception, formatter);
    }

    private sealed record FailResult(string Message) : IFailResult;

    // ---------------------------------------------------------------- ErrorReporter

    private static (ErrorReporter Reporter, BotMetrics Metrics, CapturingLogger<ErrorReporter> Log) Reporter()
    {
        var metrics = new BotMetrics();
        var log = new CapturingLogger<ErrorReporter>();
        return (new ErrorReporter(metrics, log), metrics, log);
    }

    [Fact]
    public void A_user_facing_error_shows_its_own_message_logs_at_info_and_is_counted_as_user()
    {
        var (reporter, metrics, log) = Reporter();

        var shown = reporter.Report(new SearchException("No songs found."), "play");

        Assert.Equal("No songs found.", shown);
        Assert.Equal(LogLevel.Information, Assert.Single(log.Inner.Entries).Level);
        Assert.Equal(1, metrics.Aggregates.ByTag(MetricNames.Errors)["play.user"]);
    }

    [Fact]
    public void A_user_facing_error_with_a_cause_is_a_warning_with_the_cause_attached()
    {
        var (reporter, _, log) = Reporter();
        var cause = new HttpRequestException("503");

        var shown = reporter.Report(new BooruUnavailableException("e621 is having problems.", cause), "e621");

        Assert.Equal("e621 is having problems.", shown);
        var entry = Assert.Single(log.Inner.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Same(cause, entry.Exception);
    }

    [Fact]
    public void A_bug_shows_only_a_generic_message_logs_at_error_with_the_stack_and_is_counted_as_unexpected()
    {
        var (reporter, metrics, log) = Reporter();
        var bug = new NullReferenceException("secret internals");

        var shown = reporter.Report(bug, "play");

        Assert.Equal(ErrorReporter.GenericMessage, shown);
        Assert.DoesNotContain("secret internals", shown);
        var entry = Assert.Single(log.Inner.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Same(bug, entry.Exception);
        Assert.Equal(1, metrics.Aggregates.ByTag(MetricNames.Errors)["play.unexpected"]);
    }

    [Fact]
    public void Wrapped_exceptions_are_unwrapped_before_deciding()
    {
        var (reporter, _, _) = Reporter();

        Assert.Equal("nope", reporter.Report(new TargetInvocationException(new SearchException("nope")), "play"));
        Assert.Equal("nope", reporter.Report(new AggregateException(new SearchException("nope")), "play"));
    }

    [Fact]
    public void Record_logs_an_error_without_a_user_message()
    {
        var (reporter, metrics, log) = Reporter();

        reporter.Record(new IOException("disk"), "shutdown.sessions", "Closing failed.");

        Assert.Equal(LogLevel.Error, Assert.Single(log.Inner.Entries).Level);
        Assert.Equal(1, metrics.Aggregates.ByTag(MetricNames.Errors)["shutdown.sessions"]);
    }

    // ---------------------------------------------------------------- command failures (CommandErrorHandler)

    [Fact]
    public void A_successful_command_result_produces_no_reply()
    {
        var (reporter, metrics, _) = Reporter();

        Assert.Null(CommandErrorHandler.Decide(SuccessResult.Instance, "play", reporter, metrics, NullLogger.Instance));
    }

    [Fact]
    public void A_failed_precondition_tells_the_user_the_frameworks_message_and_is_counted_as_refused()
    {
        var (reporter, metrics, _) = Reporter();
        var logger = new CapturingLogger();

        var message = CommandErrorHandler.Decide(new FailResult("You must be in a voice channel!"), "skip", reporter, metrics, logger);

        Assert.Equal("You must be in a voice channel!", message);
        Assert.Equal(LogLevel.Information, Assert.Single(logger.Entries).Level);
        Assert.Equal(1, metrics.Aggregates.ByTag(MetricNames.Errors)["skip.refused"]);
    }

    [Fact]
    public void An_exception_from_a_command_goes_through_the_reporter()
    {
        var (reporter, metrics, _) = Reporter();

        var user = CommandErrorHandler.Decide(new ExecutionExceptionResult(new SessionClosedException()), "stop", reporter, metrics, NullLogger.Instance);
        var bug = CommandErrorHandler.Decide(new ExecutionExceptionResult(new InvalidOperationException("x")), "stop", reporter, metrics, NullLogger.Instance);

        Assert.Contains("shutting down", user);
        Assert.Equal(ErrorReporter.GenericMessage, bug);
    }

    private sealed class SuccessResult : IExecutionResult
    {
        internal static readonly SuccessResult Instance = new();
    }

    // ---------------------------------------------------------------- the exception taxonomy

    [Fact]
    public void Every_known_user_message_type_is_user_facing_and_bugs_are_not()
    {
        Assert.IsAssignableFrom<UserFacingException>(new SearchException("x"));
        Assert.IsAssignableFrom<UserFacingException>(new SessionClosedException());
        Assert.IsAssignableFrom<UserFacingException>(new VoiceJoinException("x"));
        Assert.IsAssignableFrom<UserFacingException>(new LookupFailedException("x"));
        Assert.IsAssignableFrom<UserFacingException>(new StreamUnavailableException("x"));
        Assert.IsAssignableFrom<UserFacingException>(new MediaStreamException("x"));
        Assert.IsAssignableFrom<UserFacingException>(new BooruUnavailableException("x"));
        Assert.IsNotAssignableFrom<UserFacingException>(new InvalidOperationException());
        Assert.IsNotAssignableFrom<UserFacingException>(new ResolverFailedException("x")); // wording is decided by the caller
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "rate-limiting")]
    [InlineData(HttpStatusCode.Forbidden, "refused")]
    [InlineData(HttpStatusCode.NotFound, "no longer has")]
    public void FailureDescriber_explains_http_statuses(HttpStatusCode status, string expected) =>
        Assert.Contains(expected, FailureDescriber.Describe(new HttpRequestException("x", null, status)));

    [Fact]
    public void FailureDescriber_covers_the_other_cases()
    {
        Assert.Contains("network", FailureDescriber.Describe(new HttpRequestException("x")));
        Assert.Contains("timed out", FailureDescriber.Describe(new TimeoutException()));
        Assert.Equal("because", FailureDescriber.Describe(new ResolverFailedException("because")));
        Assert.Equal("said so", FailureDescriber.Describe(new SearchException("said so")));
        Assert.Contains("unexpected", FailureDescriber.Describe(new InvalidOperationException("secret")));
        Assert.DoesNotContain("secret", FailureDescriber.Describe(new InvalidOperationException("secret")));
    }

    // ---------------------------------------------------------------- e621

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }

    private static E621Client Client(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) =>
        new(new HttpClient(new StubHandler(respond)), NullLogger<E621Client>.Instance);

    private static Task<HttpResponseMessage> Status(HttpStatusCode status, string body = "") =>
        Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "rate-limiting")]
    [InlineData(HttpStatusCode.Forbidden, "refused")]
    [InlineData(HttpStatusCode.InternalServerError, "having problems")]
    [InlineData(HttpStatusCode.BadGateway, "having problems")]
    [InlineData(HttpStatusCode.Conflict, "unexpected answer")]
    public async Task An_e621_failure_is_reported_as_a_failure_not_as_no_results(HttpStatusCode status, string expected)
    {
        var client = Client(_ => Status(status));

        var ex = await Assert.ThrowsAsync<BooruUnavailableException>(() => client.GetPostsAsync("cat"));

        Assert.Contains(expected, ex.Message);
        Assert.IsType<HttpRequestException>(ex.InnerException); // kept for the operator's log
    }

    [Fact]
    public async Task Unreachable_unreadable_and_slow_answers_each_have_their_own_message()
    {
        var unreachable = Client(_ => throw new HttpRequestException("dns"));
        Assert.Contains("couldn't reach", (await Assert.ThrowsAsync<BooruUnavailableException>(() => unreachable.GetPostsAsync("x"))).Message);

        var garbage = Client(_ => Status(HttpStatusCode.OK, "<html>not json</html>"));
        Assert.Contains("couldn't read", (await Assert.ThrowsAsync<BooruUnavailableException>(() => garbage.GetPostsAsync("x"))).Message);

        var slow = Client(_ => throw new TaskCanceledException("timeout"));
        Assert.Contains("too long", (await Assert.ThrowsAsync<BooruUnavailableException>(() => slow.GetPostsAsync("x"))).Message);
    }

    [Fact]
    public async Task A_caller_cancellation_is_not_turned_into_a_booru_error()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var client = Client(_ => throw new TaskCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetPostsAsync("x", 10, cts.Token));
    }

    [Fact]
    public async Task An_empty_but_valid_answer_is_just_empty()
    {
        var client = Client(_ => Status(HttpStatusCode.OK, """{"posts":[]}"""));

        Assert.Empty(await client.GetPostsAsync("nothing matches this"));
    }
}
