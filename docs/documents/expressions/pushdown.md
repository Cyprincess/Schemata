# Pushdown and Residual Evaluation

Pushdown lets a resource query apply the backend-safe part of a filter before local evaluation. The planner keeps correctness by splitting a parsed expression into a pushed predicate and a residual predicate.

## Split semantics

`IExpressionPushdownPlanner.Plan(tree, capabilities)` returns an `ExpressionPushdownPlan`:

```csharp
public sealed record ExpressionPushdownPlan(IExpressionTree? Pushed, IExpressionTree? Residual)
{
    public bool HasResidual => Residual is not null;
    public bool HasPushed => Pushed is not null;
}
```

The pushed tree is a weakening of the original expression. Every row accepted by the original expression is also accepted by the pushed tree. Applying the pushed tree first and then the residual yields the same result as evaluating the original expression in one pass.

Three shapes are possible:

| Shape                                | Meaning                                                                   |
| ------------------------------------ | ------------------------------------------------------------------------- |
| `Pushed != null`, `Residual == null` | The backend can translate the whole expression.                           |
| `Pushed == null`, `Residual != null` | Nothing can safely push; local residual sees the backend query unchanged. |
| `Pushed != null`, `Residual != null` | The backend narrows to a superset; local residual finishes the filter.    |

Resource list handling uses this shape only when the resolved `FilteringMode` is `Residual`. `Strict` mode compiles the whole parsed tree directly.

## AIP planner

`AipPushdownPlanner` works on the AIP `Filter` AST. It splits only at the top-level conjunction. Each `AND` conjunct that is safe for the backend moves into `Pushed`; the remaining conjuncts move into `Residual`.

A conjunct pushes when all of these conditions hold:

- the node is a flat-field `Restriction`, not a navigation chain;
- a bare field uses `ExpressionCapabilities.Presence`;
- `:` uses `Presence` plus `Membership`;
- `=` and `!=` use `Comparison`, and wildcard text additionally needs `Wildcard`;
- `<`, `<=`, `>`, and `>=` use `Comparison`;
- disjunction, negation, and nested filters push only when `Logical` is enabled and every child is pushable.

Navigation chains stay residual because AIP null-chain behavior can diverge from backend three-valued logic. Function calls stay residual unless a future planner proves a specific function safe.

When the planner rebuilds partial filters, it appends `\u0001P` to the pushed source and `\u0001R` to the residual source. `ExpressionCacheKey` includes `Filter.Source`, so those suffixes keep the two compiled trees in distinct cache entries.

## CEL planner

`CelPushdownPlanner` works on `CelNode`. With `ExpressionCapabilities.Logical` enabled, it flattens top-level `&&` into conjuncts and splits those conjuncts the same way AIP does. With `Logical` disabled, it degrades to whole-or-nothing because combining several pushed clauses already requires a backend conjunction.

The CEL planner is conservative by node kind:

| Node kind                                                             | Pushability                                                                                                    |
| --------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------- |
| `CelConstant`, `CelIdentifier`                                        | Pushable.                                                                                                      |
| `CelMember`                                                           | Residual; member/null behavior can differ from backend SQL logic.                                              |
| Comparison `CelBinary`                                                | Pushes when `Comparison` is enabled, both sides are pushable, and a flat field participates.                   |
| Logical `CelBinary`                                                   | Pushes when `Logical` is enabled and both sides are pushable.                                                  |
| Arithmetic `CelBinary`                                                | Pushes when `Arithmetic` is enabled, both sides are pushable, and a flat field participates.                   |
| `in`                                                                  | Pushes when `Membership` is enabled and both sides are pushable.                                               |
| `CelUnary` `!`                                                        | Pushes when `Logical` is enabled and the operand is pushable.                                                  |
| `CelUnary` `-`                                                        | Pushes when `Arithmetic` is enabled and the operand is pushable.                                               |
| `CelCall` `has(x)`                                                    | Pushes when `Presence` is enabled and `x` is a `CelIdentifier`.                                                |
| `CelMemberCall` `contains`, `startsWith`, `endsWith`                  | Pushes when `StringMatch` is enabled, target is a flat identifier, and arguments are constants or identifiers. |
| `matches`, macros, conditionals, list literals, map literals, indexes | Residual.                                                                                                      |

Partial CEL plans also append `\u0001P` and `\u0001R` to `CelNode.Source` so pushed and residual compilations do not share one cache key.

## Residual paging

`ResidualPage.ScanAsync` streams the backend superset and applies a local residual predicate:

```csharp
var scan = await ResidualPage.ScanAsync(
    superset,
    residual,
    skip,
    pageSize,
    cap,
    countExact,
    ct);
```

Paging is applied after the residual, not before it. The scan skips `skip` rows that pass the residual, collects `pageSize` rows, and then reads one extra passing row to decide `HasMore`.

When `countExact` is false, scanning stops as soon as the page and look-ahead are known. The returned `Total` is null. When `countExact` is true, scanning continues to the end of the superset and returns the exact residual-passing total.

The `cap` bounds source rows scanned, not rows returned. Reaching the cap before the page or exact count is known throws `InvalidOperationException` with the configured limit. The resolver defaults this cap to `10_000` rows unless descriptor, profile, or entry configuration supplies a positive override.

## Custom planner guide

Implement `IExpressionPushdownPlanner` when a language can translate some constructs to the backend query:

```csharp
public sealed class MyPushdownPlanner : IExpressionPushdownPlanner
{
    public string Language => MyLanguage.Name;

    public ExpressionPushdownPlan Plan(IExpressionTree tree, ExpressionCapabilities capabilities) {
        if (tree is not MyTree node) {
            throw new ArgumentException("Tree must be a MyTree.", nameof(tree));
        }

        return Split(node, capabilities);
    }
}
```

Use these rules:

1. Prove pushability before pushing. A backend operator must preserve the language's null, error, function, and comparison semantics.
2. Split only at operators where removing one side leaves a safe superset. Top-level conjunction is safe; arbitrary disjunction is usually not.
3. Put ambiguous constructs in the residual. Local evaluation is slower but preserves results.
4. Set distinct `Source` values when you create pushed and residual ASTs from the same original expression.
5. Register the planner with the same language key as the compiler and descriptor:

```csharp
services.AddKeyedSingleton<IExpressionPushdownPlanner, MyPushdownPlanner>(MyLanguage.Name);
```

Pair the planner with a descriptor/profile `FilteringMode.Residual` setting. In `Strict`, resource list compilation bypasses the planner.

## See also

- [Expressions Overview](overview.md)
- [AIP Expressions](aip.md)
- [CEL Expressions](cel.md)
- [Custom Language](custom-language.md)
- [Resource Filtering](../resource/filtering.md)
