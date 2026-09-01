namespace Schemata.Insight.Skeleton.Models;

/// <summary>The unified type for every expression slot: source text plus an optional language override.</summary>
/// <param name="Source">The expression source text.</param>
/// <param name="Language">The expression language, or null to fall back to the request or module default.</param>
public sealed record InsightExpression(string Source, string? Language = null);