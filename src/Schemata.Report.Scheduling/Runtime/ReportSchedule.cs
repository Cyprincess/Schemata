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

    internal string SlotKey => ReportJobKeyResolver.Slot(CanonicalName, Name);
}

/// <summary>Arms and disarms report schedules through the closed job entity types.</summary>
/// <typeparam name="TJob">The persisted job entity type registered by the host.</typeparam>
/// <typeparam name="TExecution">The persisted execution entity type registered by the host.</typeparam>
internal static class ReportSchedule<TJob, TExecution>
    where TJob : SchemataJob, new()
    where TExecution : SchemataJobExecution, new()
{
    internal static Task ArmAsync(IScheduler<TJob, TExecution> scheduler, ReportScheduleProjection projection, CancellationToken ct) {
        var name = GetName(projection.Name);
        return scheduler.ScheduleAsync(CreateJob(projection, name), new Dictionary<string, string?> {
            ["report"] = projection.CanonicalName ?? name,
        }, ct);
    }

    internal static async Task DisarmAsync(
        IScheduler<TJob, TExecution> scheduler, IRepository<TJob> jobs, ReportScheduleProjection projection, CancellationToken ct
    ) {
        if (string.IsNullOrWhiteSpace(projection.Name)) {
            return;
        }

        var job = await jobs.FirstOrDefaultAsync(query => query.Where(row => row.Key == projection.SlotKey), ct);
        if (job is not null) {
            await scheduler.UnscheduleAsync(job.CanonicalName!, ct);
        }
    }

    private static TJob CreateJob(ReportScheduleProjection projection, string name) {
        var job = new TJob {
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
