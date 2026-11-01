using System.Collections.Generic;
using Schemata.Abstractions.Resource;
using Schemata.Expressions.Skeleton;
using Schemata.Insight.Skeleton.Catalog;

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
    ///     The <c>total_size</c> computation mode; <see cref="TotalSizeMode.Default" /> behaves as
    ///     <see cref="TotalSizeMode.Exact" />, mirroring Resource.
    /// </summary>
    public TotalSizeMode TotalSize { get; set; }

    /// <summary>
    ///     The expression languages this module enables, in priority order; the first is the default
    ///     when a slot and the request both omit one, and its per-language filtering and residual
    ///     scan settings govern execution.
    /// </summary>
    public ExpressionLanguageProfile Expressions { get; set; } = new();
}
