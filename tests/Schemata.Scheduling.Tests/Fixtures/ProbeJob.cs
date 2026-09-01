using System.Threading;
using System.Threading.Tasks;
using Schemata.Scheduling.Skeleton;

namespace Schemata.Scheduling.Tests.Fixtures;

public sealed class ProbeJob<TPayload> : IScheduledJob
    where TPayload : class
{
    public Task ExecuteAsync(JobContext context, CancellationToken ct) { return Task.CompletedTask; }
}