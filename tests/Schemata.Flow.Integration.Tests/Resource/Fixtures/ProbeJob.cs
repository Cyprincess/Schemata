using System.Threading;
using System.Threading.Tasks;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Attributes;

namespace Schemata.Flow.Integration.Tests.Resource.Fixtures;

[ScheduledJob(JobKey)]
public sealed class ProbeJob : IScheduledJob
{
    public const string JobKey = "resource-http-probe";

    public Task ExecuteAsync(JobContext context, CancellationToken ct) { return Task.CompletedTask; }
}
