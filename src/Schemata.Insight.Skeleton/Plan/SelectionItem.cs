using System.Collections.Immutable;

namespace Schemata.Insight.Skeleton;

/// <summary>The kind of a selection item.</summary>
public enum SelectionKind
{
    Field,
    Expression,
    Nested,
}

/// <summary>
///     One projected item: a field path, a computed value expression, or a nested object with its own
///     child items and a compiled sub-plan for its local transformations.
/// </summary>
/// <param name="Alias">The output key.</param>
/// <param name="Kind">Whether the item is a field, a computed expression, or a nested object.</param>
/// <param name="FieldPath">The source field path for <see cref="SelectionKind.Field" />/<see cref="SelectionKind.Nested" />.</param>
/// <param name="Expression">The value expression for <see cref="SelectionKind.Expression" />.</param>
/// <param name="Children">The child items for <see cref="SelectionKind.Nested" />.</param>
/// <param name="Nested">The compiled sub-plan for a nested item's local transformations.</param>
public sealed record SelectionItem(
    string                        Alias,
    SelectionKind                 Kind,
    string?                       FieldPath,
    ParsedExpression?             Expression,
    ImmutableArray<SelectionItem> Children,
    PlanNode?                     Nested);
