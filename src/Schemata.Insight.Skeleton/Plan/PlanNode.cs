using System.Collections.Immutable;
using Schemata.Expressions.Skeleton;

namespace Schemata.Insight.Skeleton;

/// <summary>Whether an expression slot yields a boolean predicate or a scalar value.</summary>
public enum ExpressionKind
{
    Predicate,
    Value,
}

/// <summary>A parsed expression slot: the language-agnostic tree plus its resolved language and kind.</summary>
/// <param name="Tree">The parsed expression tree.</param>
/// <param name="Language">The resolved expression language.</param>
/// <param name="Kind">Whether the slot is a predicate or a value.</param>
public sealed record ParsedExpression(IExpressionTree Tree, string Language, ExpressionKind Kind);

/// <summary>A logical plan node. Each node records the set of source aliases its subtree touches.</summary>
public abstract record PlanNode
{
    /// <summary>The source aliases referenced by this node's subtree.</summary>
    public ImmutableHashSet<string> SourceSet { get; init; } = ImmutableHashSet<string>.Empty;
}
