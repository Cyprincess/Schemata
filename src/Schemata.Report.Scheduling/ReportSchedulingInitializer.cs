using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Schemata.Report.Scheduling.Runtime;
using Schemata.Report.Skeleton;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Report.Scheduling;

/// <summary>Arms every periodic report definition when the host starts.</summary>
/// <typeparam name="TJob">The persisted job entity type registered by the host.</typeparam>
/// <typeparam name="TExecution">The persisted execution entity type registered by the host.</typeparam>
public sealed class ReportSchedulingInitializer<TJob, TExecution>(
    IReportDefinitionStore      store,
    IScheduler<TJob, TExecution> scheduler
) : IHostedService
    where TJob : SchemataJob, new()
    where TExecution : SchemataJobExecution, new()
{
    public async Task StartAsync(CancellationToken ct) {
        await foreach (var report in store.ListPeriodicAsync(ct)) {
            var projection = ReportScheduleProjection.Capture(report);
            if (projection.ActivePeriodic) {
                await ReportSchedule<TJob, TExecution>.ArmAsync(scheduler, projection, ct);
            }
        }
    }

    public Task StopAsync(CancellationToken ct) {
        return Task.CompletedTask;
    }
}
