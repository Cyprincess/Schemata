using System;
using System.Collections.Generic;

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