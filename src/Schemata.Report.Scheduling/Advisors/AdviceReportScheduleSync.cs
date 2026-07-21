using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Report.Scheduling.Internal;
using Schemata.Report.Skeleton;
using Schemata.Scheduling.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Report.Scheduling.Advisors;

/// <summary>Synchronizes a report's recurring scheduler entry after its definition commits.</summary>
/// <typeparam name="TReport">Persisted report-definition entity type.</typeparam>
/// <remarks>
///     A committed advisor runs after a successful commit; the host initializer re-arms persisted periodic
///     definitions on startup.
/// </remarks>
public sealed class AdviceReportScheduleSync<TReport>(IScheduler scheduler) : IRepositoryCommittedAdvisor<TReport>
    where TReport : SchemataReport
{
    /// <inheritdoc />
    public int Order => Orders.Extension;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        IRepository<TReport>   repository,
        CommitChanges<TReport> changes,
        CancellationToken      ct = default
    ) {
        foreach (var report in changes.Updated) {
            await scheduler.UnscheduleAsync(ReportSchedule.JobCanonicalName(report), ct);
            if (report.Periodic) {
                await ReportSchedule.ArmAsync(scheduler, report, ct);
            }
        }

        foreach (var report in changes.Removed) {
            await scheduler.UnscheduleAsync(ReportSchedule.JobCanonicalName(report), ct);
        }

        return AdviseResult.Continue;
    }
}
