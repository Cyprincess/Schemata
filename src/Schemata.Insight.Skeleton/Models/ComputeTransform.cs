using System.Collections.Immutable;

namespace Schemata.Insight.Skeleton.Models;

/// <summary>Adds computed fields.</summary>
public sealed record ComputeTransform(ImmutableArray<ComputedFieldSpec> Fields);