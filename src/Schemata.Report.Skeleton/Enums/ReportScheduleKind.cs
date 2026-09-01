namespace Schemata.Report.Skeleton.Enums;

/// <summary>Describes the recurrence form of a periodic report.</summary>
public enum ReportScheduleKind
{
    /// <summary>The report runs at a fixed interval.</summary>
    Periodic,

    /// <summary>The report runs according to a cron expression.</summary>
    Cron,
}
