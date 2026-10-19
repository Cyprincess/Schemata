using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Scheduling.Skeleton;

namespace Schemata.Scheduling.Integration.Tests.Fixtures;

public sealed class FailingJob : IScheduledJob
{
    public const string Key = "jobs.failing";

    public int Attempts { get; private set; }

    public Task ExecuteAsync(JobContext context, CancellationToken ct) {
        Attempts++;
        throw new InvalidOperationException("body exploded");
    }
}
