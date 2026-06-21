using System.Collections.Generic;
using Schemata.Abstractions.Resource;
using Schemata.Expressions.Skeleton;
using Schemata.Insight.Skeleton;

namespace Schemata.Insight.Foundation;

/// <summary>
///     Options for the Schemata Insight module.
/// </summary>
public sealed class SchemataInsightOptions
{
    /// <summary>
    ///     The registered sources keyed by name; the catalog resolves caller-supplied names against
    ///     this map.
    /// </summary>
    public IDictionary<string, SourceConfig> Sources { get; } = new Dictionary<string, SourceConfig>();

    /// <summary>
    ///     The expression language used when a slot and the request both omit one; defaults to AIP.
    /// </summary>
    public string DefaultLanguage { get; set; } = ExpressionLanguages.Aip;

    /// <summary>
    ///     The <c>total_size</c> computation mode; <see cref="TotalSizeMode.Default" /> behaves as
    ///     <see cref="TotalSizeMode.Exact" />, mirroring Resource.
    /// </summary>
    public TotalSizeMode TotalSize { get; set; }

    /// <summary>
    ///     The cap on rows scanned during local residual paging; default 10 000.
    /// </summary>
    public int MaxResidualScanRows { get; set; } = 10_000;
}
