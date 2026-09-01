using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Scheduling.Skeleton;

namespace Schemata.Scheduling.Integration.Tests.Fixtures;

public sealed class BlockingJob : IScheduledJob
{
    public const string Key = "jobs.blocking";

    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(30);

    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    #region IScheduledJob Members

    public async Task ExecuteAsync(JobContext context, CancellationToken ct) {
        Entered.SetResult();
        await Release.Task.WaitAsync(ReleaseTimeout, ct);
    }

    #endregion
}
