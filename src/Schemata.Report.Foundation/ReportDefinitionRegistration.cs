using Schemata.Report.Skeleton;
using Schemata.Report.Skeleton.Models;
using Schemata.Insight.Skeleton.Queries;
using Schemata.Report.Foundation.Definitions;
using Schemata.Report.Skeleton.Enums;

namespace Schemata.Report.Foundation;

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