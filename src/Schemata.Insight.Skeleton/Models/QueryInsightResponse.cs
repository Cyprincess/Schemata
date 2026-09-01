using System.Collections.Generic;
using System.Collections.Immutable;

namespace Schemata.Insight.Skeleton.Models;

/// <summary>A federated read query result: nested rows, a schema tree, and pagination metadata.</summary>
public sealed class QueryInsightResponse
{
    /// <summary>The result rows; each is a nested string-keyed map.</summary>
    public IList<IReadOnlyDictionary<string, object?>> Rows { get; set; } = [];

    /// <summary>The nested schema describing the row shape.</summary>
    public ImmutableArray<FieldDescriptor> Schema { get; set; } = [];

    /// <summary>The continuation token, or null when the result is exhausted.</summary>
    public string? NextPageToken { get; set; }

    /// <summary>The best-effort total row count, or null when not computed.</summary>
    public int? TotalSize { get; set; }

    /// <summary>The sources that could not be reached (AIP-217).</summary>
    public IList<string> Unreachable { get; set; } = [];
}
