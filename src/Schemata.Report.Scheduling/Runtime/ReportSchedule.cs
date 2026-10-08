using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Report.Foundation.Jobs;
using Schemata.Report.Skeleton.Entities;
using Schemata.Report.Skeleton.Enums;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Report.Scheduling.Runtime;

/// <summary>
///     Immutable capture of a report's schedule-relevant configuration for one mutation. Captured at
///     staging time so a later in-place mutation of the same entity cannot rewrite this operation's
///     arm/disarm semantics.
/// </summary>
internal sealed record ReportScheduleProjection(
    string?            Name,
    string?            CanonicalName,
    bool               ActivePeriodic,
    ReportScheduleKind ScheduleKind,
    string?            CronExpression,
    long?              IntervalTicks
)
{
    internal static ReportScheduleProjection Capture(SchemataReport report) {
        var active = report is not Schemata.Abstractions.Entities.ISoftDelete deleted || deleted.DeleteTime is null;
        return new(report.Name, report.CanonicalName, report.Periodic && active, report.ScheduleKind, report.CronExpression, report.IntervalTicks);
    }

    /// <summary>
    ///     The schedule-slot key: the report's canonical name when persisted, its public name for
    ///     configuration-only definitions. Canonical names are unique among active instances, so two
    ///     reports never share a slot.
    /// </summary>
    internal string SlotKey => $"report:{CanonicalName ?? Name}";
}

internal static class ReportSchedule
{
    internal static Task ArmAsync(IScheduler scheduler, ReportScheduleProjection projection, CancellationToken ct) {
        var name = GetName(projection.Name);
        return scheduler.ScheduleAsync(CreateJob(projection, name), new Dictionary<string, string?> {
            ["report"] = projection.CanonicalName ?? name,
        }, ct);
    }

    internal static async Task DisarmAsync(
        IScheduler scheduler, IRepository<SchemataJob> jobs, ReportScheduleProjection projection, CancellationToken ct
    ) {
        if (string.IsNullOrWhiteSpace(projection.Name)) {
            return;
        }

        var job = await jobs.FirstOrDefaultAsync(query => query.Where(row => row.Key == projection.SlotKey), ct);
        if (job is not null) {
            await scheduler.UnscheduleAsync(job.CanonicalName!, ct);
        }
    }

    private static SchemataJob CreateJob(ReportScheduleProjection projection, string name) {
        var job = new SchemataJob {
            Key           = projection.SlotKey,
            JobKey        = ReportJobKeyResolver.Key,
            State         = JobState.Active,
        };
        ScheduleDefinitionMapper.ApplyToJob(CreateDefinition(projection, name), job);
        return job;
    }

    private static IScheduleDefinition CreateDefinition(ReportScheduleProjection projection, string name) {
        return projection.ScheduleKind switch {
            ReportScheduleKind.Cron when !string.IsNullOrWhiteSpace(projection.CronExpression) => new CronSchedule(projection.CronExpression),
            ReportScheduleKind.Cron => throw new InvalidOperationException($"Periodic report '{name}' requires a cron expression."),
            ReportScheduleKind.Periodic when projection.IntervalTicks is > 0 => new PeriodicSchedule(TimeSpan.FromTicks(projection.IntervalTicks.Value)),
            ReportScheduleKind.Periodic => throw new InvalidOperationException($"Periodic report '{name}' requires a positive interval."),
            var kind => throw new InvalidOperationException($"Periodic report '{name}' has an unsupported schedule kind '{kind}'."),
        };
    }

    private static string GetName(string? name) {
        return !string.IsNullOrWhiteSpace(name)
            ? name
            : throw new InvalidOperationException("Periodic report requires a name.");
    }
}
