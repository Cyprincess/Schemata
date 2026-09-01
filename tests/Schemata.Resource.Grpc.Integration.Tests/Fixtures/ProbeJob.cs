using System.Threading;
using System.Threading.Tasks;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Attributes;

namespace Schemata.Resource.Grpc.Integration.Tests.Fixtures;

[ScheduledJob(JobKey)]
public sealed class ProbeJob : IScheduledJob
{
    public const string JobKey = "resource-grpc-probe";

    public Task ExecuteAsync(JobContext context, CancellationToken ct) { return Task.CompletedTask; }
}
