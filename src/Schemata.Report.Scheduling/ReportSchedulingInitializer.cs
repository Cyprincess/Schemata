using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Schemata.Report.Scheduling.Internal;
using Schemata.Report.Skeleton;
using Schemata.Scheduling.Skeleton;

namespace Schemata.Report.Scheduling;

/// <summary>Arms every periodic report definition when the host starts.</summary>
public sealed class ReportSchedulingInitializer(IReportDefinitionStore definitions, IScheduler scheduler) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken ct) {
        await foreach (var report in definitions.ListPeriodicAsync(ct)) {
            await ReportSchedule.ArmAsync(scheduler, report, ct);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken ct) {
        return Task.CompletedTask;
    }
}
