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

internal static class ReportSchedule
{
    internal static Task ArmAsync(IScheduler scheduler, SchemataReport report, CancellationToken ct) {
        var name = GetName(report);
        return scheduler.ScheduleAsync(CreateJob(report, name), new Dictionary<string, string?> {
            ["report"] = name,
        }, ct);
    }

    internal static async Task DisarmAsync(
        IScheduler scheduler, IRepository<SchemataJob> jobs, SchemataReport report, CancellationToken ct
    ) {
        var key = $"report:{GetName(report)}";
        var job = await jobs.FirstOrDefaultAsync(query => query.Where(row => row.Key == key), ct);
        if (job is not null) {
            await scheduler.UnscheduleAsync(job.CanonicalName!, ct);
        }
    }

    private static SchemataJob CreateJob(SchemataReport report, string name) {
        var job = new SchemataJob {
            Key           = $"report:{name}",
            JobKey        = ReportJobKeyResolver.Key,
            State         = JobState.Active,
        };
        ScheduleDefinitionMapper.ApplyToJob(CreateDefinition(report, name), job);
        return job;
    }

    private static IScheduleDefinition CreateDefinition(SchemataReport report, string name) {
        return report.ScheduleKind switch {
            ReportScheduleKind.Cron when !string.IsNullOrWhiteSpace(report.CronExpression) => new CronSchedule(report.CronExpression),
            ReportScheduleKind.Cron => throw new InvalidOperationException($"Periodic report '{name}' requires a cron expression."),
            ReportScheduleKind.Periodic when report.IntervalTicks is > 0 => new PeriodicSchedule(TimeSpan.FromTicks(report.IntervalTicks.Value)),
            ReportScheduleKind.Periodic => throw new InvalidOperationException($"Periodic report '{name}' requires a positive interval."),
            var kind => throw new InvalidOperationException($"Periodic report '{name}' has an unsupported schedule kind '{kind}'."),
        };
    }

    private static string GetName(SchemataReport report) {
        return !string.IsNullOrWhiteSpace(report.Name)
            ? report.Name
            : throw new InvalidOperationException("Periodic report requires a name.");
    }
}
