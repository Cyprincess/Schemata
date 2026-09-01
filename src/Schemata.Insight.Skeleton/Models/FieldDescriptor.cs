using System.Collections.Immutable;

namespace Schemata.Insight.Skeleton.Models;

/// <summary>Describes one response field; nested objects carry child descriptors.</summary>
/// <param name="Name">The field name (the row key / parent selection alias).</param>
/// <param name="Type">The field type.</param>
/// <param name="SourceAlias">The originating source alias; null for aggregated or computed fields.</param>
/// <param name="IsList">Whether the field holds a list of values.</param>
/// <param name="Children">Child descriptors for a nested object.</param>
public sealed record FieldDescriptor(
    string                          Name,
    FieldType                       Type,
    string?                         SourceAlias,
    bool                            IsList,
    ImmutableArray<FieldDescriptor> Children);