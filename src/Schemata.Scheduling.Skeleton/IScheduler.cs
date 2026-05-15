using System.Threading;
using System.Threading.Tasks;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Skeleton;

public interface IScheduler
{
    Task StartAsync(CancellationToken ct);

    Task StopAsync(CancellationToken ct);

    Task ScheduleJobAsync(SchemataJob job, CancellationToken ct);

    Task UnscheduleJobAsync(string jobName, CancellationToken ct);
}
