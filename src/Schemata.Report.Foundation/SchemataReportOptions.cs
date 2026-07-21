using System;
using System.Collections.Generic;
using Schemata.Insight.Skeleton;
using Schemata.Report.Skeleton;

namespace Schemata.Report.Foundation;

/// <summary>Options that bound persisted report chunks and inline report results.</summary>
public sealed class SchemataReportOptions
{
    /// <summary>Maximum rows encoded into one persisted snapshot chunk.</summary>
    public int ChunkSize { get; set; } = 1_000;

    /// <summary>Maximum rows returned for an inline report.</summary>
    public int MaxInlineRows { get; set; } = 10_000;

    /// <summary>Maximum rows one snapshot <c>:read</c> page returns; larger requests are clamped to this bound.</summary>
    public int MaxReadPageSize { get; set; } = 1_000;

    /// <summary>
    ///     Grace period before retention reclaims chunks from failed or cancelled snapshots.
    /// </summary>
    /// <remarks>
    ///     The default gives operators one day to inspect incomplete materializations before cleanup removes
    ///     their headers and chunks.
    /// </remarks>
    public TimeSpan IncompleteSnapshotGracePeriod { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    ///     Configuration-time report definitions. DSL registrations append to this list before the host is built;
    ///     configuration definitions take precedence over persisted database definitions with the same name.
    /// </summary>
    public IList<ReportDefinitionRegistration> Definitions { get; } = [];
}

/// <summary>
///     A configuration-time report definition consumed by <see cref="ConfigurationReportDefinitionStore" />.
/// </summary>
/// <remarks>
///     The DSL appends registrations to <see cref="SchemataReportOptions.Definitions" />. Expression definitions set
///     <see cref="Query" />; program definitions set <see cref="Provider" /> to the keyed provider registration.
/// </remarks>
public sealed record ReportDefinitionRegistration
{
    /// <summary>Unique report leaf name.</summary>
    public required string Name { get; init; }

    /// <summary>Whether the definition is an inline expression or a keyed program provider.</summary>
    public ReportSourceKind SourceKind { get; init; } = ReportSourceKind.Expression;

    /// <summary>Whether this definition is eligible for periodic scheduling.</summary>
    public bool Periodic { get; init; }

    /// <summary>Schedule representation used when <see cref="Periodic" /> is enabled.</summary>
    public ReportScheduleKind ScheduleKind { get; init; }

    /// <summary>Cron expression for cron-backed periodic definitions.</summary>
    public string? CronExpression { get; init; }

    /// <summary>Interval length in ticks for periodic definitions.</summary>
    public long? IntervalTicks { get; init; }

    /// <summary>Snapshot retention limits for the definition.</summary>
    public ReportRetention? Retention { get; init; }

    /// <summary>Expression-backed query definition.</summary>
    public QueryInsightRequest? Query { get; init; }

    /// <summary>Key of the program-backed <see cref="IReportDefinitionProvider" />.</summary>
    public string? Provider { get; init; }
}
