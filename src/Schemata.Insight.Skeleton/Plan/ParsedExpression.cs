using Schemata.Expressions.Skeleton;

namespace Schemata.Insight.Skeleton.Plan;

/// <summary>A parsed expression slot: the language-agnostic tree plus its resolved language and kind.</summary>
/// <param name="Tree">The parsed expression tree.</param>
/// <param name="Language">The resolved expression language.</param>
/// <param name="Kind">Whether the slot is a predicate or a value.</param>
public sealed record ParsedExpression(IExpressionTree Tree, string Language, ExpressionKind Kind);