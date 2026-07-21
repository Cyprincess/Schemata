namespace Schemata.Report.Skeleton;

/// <summary>Retention limits applied to persisted report snapshots.</summary>
public sealed class ReportRetention
{
    /// <summary>Maximum number of newest snapshots to retain.</summary>
    public int? MaxCount { get; set; }

    /// <summary>Maximum snapshot age in days.</summary>
    public int? MaxAgeDays { get; set; }
}
