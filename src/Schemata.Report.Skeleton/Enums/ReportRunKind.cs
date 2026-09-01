namespace Schemata.Report.Skeleton.Enums;

/// <summary>Describes how a report snapshot was generated.</summary>
public enum ReportRunKind
{
    /// <summary>The caller immediately generated and persisted the snapshot.</summary>
    ImmediatePersisted,

    /// <summary>A scheduler generated the snapshot.</summary>
    Scheduled,
}
