using System.Collections.Generic;

namespace Schemata.Insight.Skeleton;

/// <summary>
///     A nested projection item: a field path, a computed expression, or a nested sub-selection with
///     its own local transformation pipeline.
/// </summary>
public sealed class SelectionSpec
{
    /// <summary>The dotted field path (e.g. <c>o.customer.name</c>); null for a computed item.</summary>
    public string? Field { get; set; }

    /// <summary>The response key; omitted uses the last path segment.</summary>
    public string? Alias { get; set; }

    /// <summary>The computed expression when <see cref="Field" /> is omitted.</summary>
    public InsightExpression? Expression { get; set; }

    /// <summary>Nested child selections.</summary>
    public IList<SelectionSpec> Selections { get; set; } = [];

    /// <summary>A local transformation pipeline applied to this item before projection.</summary>
    public IList<TransformationSpec> Transformations { get; set; } = [];
}
