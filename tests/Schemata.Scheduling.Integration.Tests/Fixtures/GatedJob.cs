using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Scheduling.Skeleton;

namespace Schemata.Scheduling.Integration.Tests.Fixtures;

public sealed class GatedJob : IScheduledJob
{
    public const string Key = "jobs.gated";

    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task ExecuteAsync(JobContext context, CancellationToken ct) {
        Entered.SetResult();
        await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
    }
}