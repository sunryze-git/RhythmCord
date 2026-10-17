using Microsoft.Extensions.Logging;

namespace Singularity.Infrastructure;

internal static class TaskExtensions
{
    /// <summary>
    ///     Marks a fire-and-forget task's exception as observed so it never surfaces as an unobserved task exception.
    ///     If a logger is given the failure is recorded at Debug, so it is not lost entirely: whoever awaits the task
    ///     later is responsible for reporting it properly.
    /// </summary>
    internal static void Observe(this Task task, ILogger? logger = null, string? what = null) =>
        task.ContinueWith(t =>
        {
            if (logger is not null && t.Exception is { } failure)
                logger.LogDebug(failure.GetBaseException(), "Background work failed: {What}", what ?? "task");
        }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
}
