using System.Collections.Generic;
using System.Collections.Immutable;

namespace Schemata.Insight.Skeleton;

/// <summary>The field types a response schema can describe.</summary>
public enum FieldType
{
    Unspecified,
    String,
    Int64,
    Double,
    Bool,
    Timestamp,
    Duration,
    Bytes,
    Object = 100,
}

/// <summary>Describes one response field; nested objects carry child descriptors.</summary>
/// <param name="Name">The field name (the row key / parent selection alias).</param>
/// <param name="Type">The field type.</param>
/// <param name="SourceAlias">The originating source alias; null for aggregated or computed fields.</param>
/// <param name="IsList">Whether the field holds a list of values.</param>
/// <param name="Children">Child descriptors for a nested object.</param>
public sealed record FieldDescriptor(
    string                         Name,
    FieldType                      Type,
    string?                        SourceAlias,
    bool                           IsList,
    ImmutableArray<FieldDescriptor> Children);

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
