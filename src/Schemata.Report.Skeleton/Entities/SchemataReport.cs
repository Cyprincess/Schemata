using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Schemata.Abstractions.Entities;
using Schemata.Insight.Skeleton;

namespace Schemata.Report.Skeleton;

/// <summary>Persisted definition of an expression-backed or program-backed report.</summary>
[CanonicalName("reports/{report}")]
[DisplayName("Report")]
public class SchemataReport : IIdentifier, ICanonicalName, IDescriptive, ITimestamp, IConcurrency
{
    /// <summary>JSON-serialized <see cref="QueryInsightRequest" /> definition for expression-backed reports.</summary>
    public string? Definition { get; set; }

    /// <summary>Identifies whether this definition is expression-backed or program-backed.</summary>
    public ReportSourceKind SourceKind { get; set; }

    /// <summary>Key of the dependency-injected definition provider for program-backed reports.</summary>
    public string? Provider { get; set; }

    /// <summary>Indicates whether the report is materialized on a recurring schedule.</summary>
    public bool Periodic { get; set; }

    /// <summary>Selects the recurring schedule representation when <see cref="Periodic" /> is enabled.</summary>
    public ReportScheduleKind ScheduleKind { get; set; }

    /// <summary>Cron schedule expression for cron-based recurring reports.</summary>
    public string? CronExpression { get; set; }

    /// <summary>Recurring interval duration in ticks for periodic reports.</summary>
    public long? IntervalTicks { get; set; }

    /// <summary>Limits the count and age of persisted snapshots for this report.</summary>
    public ReportRetention? Retention { get; set; }

    #region ICanonicalName Members

    /// <summary>Leaf identifier used to construct the report canonical name.</summary>
    public string? Name { get; set; }

    /// <summary>Fully-qualified canonical name of this report.</summary>
    public string? CanonicalName { get; set; }

    #endregion

    #region IDescriptive Members

    /// <summary>Default display name for this report.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Localized display names keyed by language tag.</summary>
    public Dictionary<string, string?>? DisplayNames { get; set; }

    /// <summary>Default human-readable description for this report.</summary>
    public string? Description { get; set; }

    /// <summary>Localized descriptions keyed by language tag.</summary>
    public Dictionary<string, string?>? Descriptions { get; set; }

    #endregion

    #region IConcurrency Members

    /// <summary>Optimistic concurrency token for this definition.</summary>
    [ConcurrencyCheck]
    public Guid Timestamp { get; set; }

    #endregion

    #region IIdentifier Members

    /// <summary>Stable persistence identifier for this report.</summary>
    public Guid Uid { get; set; }

    #endregion

    #region ITimestamp Members

    /// <summary>Time at which this report was created.</summary>
    public DateTime? CreateTime { get; set; }

    /// <summary>Time at which this report was last updated.</summary>
    public DateTime? UpdateTime { get; set; }

    #endregion
}
