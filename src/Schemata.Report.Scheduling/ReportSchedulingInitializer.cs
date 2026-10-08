using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Schemata.Report.Scheduling.Runtime;
using Schemata.Report.Skeleton;
using Schemata.Scheduling.Skeleton;

namespace Schemata.Report.Scheduling;

/// <summary>Arms every periodic report definition when the host starts.</summary>
public sealed class ReportSchedulingInitializer(IReportDefinitionStore store, IScheduler scheduler) : IHostedService
{
    public async Task StartAsync(CancellationToken ct) {
        await foreach (var report in store.ListPeriodicAsync(ct)) {
            var projection = ReportScheduleProjection.Capture(report);
            if (projection.ActivePeriodic) {
                await ReportSchedule.ArmAsync(scheduler, projection, ct);
            }
        }
    }

    public Task StopAsync(CancellationToken ct) {
        return Task.CompletedTask;
    }
}
