namespace Schemata.Insight.Skeleton.Models;

/// <summary>Orders rows by an AIP-132 order-by clause (fixed syntax, no language).</summary>
public sealed record OrderByTransform(string OrderBy);