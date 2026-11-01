using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Report.Scheduling.Runtime;
using Schemata.Report.Skeleton.Entities;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using static Schemata.Abstractions.SchemataConstants;
using Operations = Schemata.Abstractions.Entities.Operations;

namespace Schemata.Report.Scheduling.Advisors;

/// <summary>
///     Synchronizes report schedules after a committed report mutation. <see cref="Prepare" /> captures an
///     immutable per-operation projection at staging time; the returned callback runs only after the owning
///     unit of work commits. Job identity is the canonical <c>report:{name}</c> key, so re-arming the same
///     report replaces its single scheduler entry instead of duplicating it.
/// </summary>
/// <typeparam name="TReport">The report entity type.</typeparam>
/// <typeparam name="TJob">The persisted job entity type registered by the host.</typeparam>
/// <typeparam name="TExecution">The persisted execution entity type registered by the host.</typeparam>
public sealed class AdviceReportScheduleSync<TReport, TJob, TExecution>(
    IScheduler<TJob, TExecution> scheduler,
    IRepository<TJob>            jobs
) : IResourceMutationCommittedAdvisor<TReport>
    where TReport : SchemataReport
    where TJob : SchemataJob, new()
    where TExecution : SchemataJobExecution, new()
{
    public int Order => Orders.Extension;

    public Func<CancellationToken, Task>? Prepare(TReport entity, Operations operation) {
        ArgumentNullException.ThrowIfNull(entity);

        var projection = ReportScheduleProjection.Capture(entity);
        var named = !string.IsNullOrWhiteSpace(projection.Name);

        return operation switch {
            Operations.Create or Operations.Undelete when projection.ActivePeriodic && named
                => ct => ReportSchedule<TJob, TExecution>.ArmAsync(scheduler, projection, ct),
            Operations.Create or Operations.Undelete
                => null,
            Operations.Update
                => async ct => {
                    await ReportSchedule<TJob, TExecution>.DisarmAsync(scheduler, jobs, projection, ct);
                    if (projection.ActivePeriodic && named) {
                        await ReportSchedule<TJob, TExecution>.ArmAsync(scheduler, projection, ct);
                    }
                },
            Operations.Delete or Operations.Expunge or Operations.Purge
                => ct => ReportSchedule<TJob, TExecution>.DisarmAsync(scheduler, jobs, projection, ct),
            _ => null,
        };
    }
}
