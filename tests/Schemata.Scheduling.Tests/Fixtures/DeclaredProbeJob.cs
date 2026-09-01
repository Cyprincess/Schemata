using System.Threading;
using System.Threading.Tasks;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Attributes;

namespace Schemata.Scheduling.Tests.Fixtures;

[ScheduledJob(JobKey)]
public sealed class DeclaredProbeJob : IScheduledJob
{
    public const string JobKey = "schemata.tests.declared";

    public Task ExecuteAsync(JobContext context, CancellationToken ct) { return Task.CompletedTask; }
}