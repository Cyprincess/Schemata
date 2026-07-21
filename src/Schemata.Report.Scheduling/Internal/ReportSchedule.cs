using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Report.Foundation;
using Schemata.Report.Skeleton;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Report.Scheduling.Internal;

internal static class ReportSchedule
{
    internal static Task ArmAsync(IScheduler scheduler, SchemataReport report, CancellationToken ct) {
        var name = GetName(report);
        return scheduler.ScheduleAsync(CreateJob(report, name), new Dictionary<string, string?> {
            ["report"] = name,
        }, ct);
    }

    internal static string JobCanonicalName(SchemataReport report) {
        return $"jobs/report-{GetName(report)}";
    }

    private static SchemataJob CreateJob(SchemataReport report, string name) {
        var job = new SchemataJob {
            Name          = $"report-{name}",
            CanonicalName = $"jobs/report-{name}",
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
