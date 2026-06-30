# Planning

`InsightPlanBuilder` turns a `QueryInsightRequest` into a logical `PlanNode` tree. The builder resolves
source names, folds joins, applies the ordered transformation pipeline, compiles expression slots, adds
a terminal selection, and wraps the plan in a top-level `LimitNode` for request paging.

## Build order

`BuildAsync` runs in this order:

1. Reject requests with no sources.
2. Resolve each `SourceBinding.Name` through the registered `IInsightSourceCatalog` instances.
3. Create one `SourceNode` per request alias.
4. Fold `JoinSpec` entries into `JoinNode` trees.
5. Apply top-level transformations in request order.
6. Build terminal `SelectionItem` entries from `SelectionSpec`.
7. Return `LimitNode(selection, request.Skip, request.PageSize)`.

Each node carries `SourceSet`, the aliases referenced by its subtree. `PlanExecutor` uses it to choose
single-source driver execution or multi-source local execution.

## SourceNode

`SourceNode(string Alias, SourceConfig Config)` is a leaf. `Alias` is the request alias (`s`, `b`,
`p`), and `Config` is the catalog result that names the driver and its parameters.

Unknown source names throw `InsightValidationException` with reason `UNKNOWN_SOURCE_NAME` and metadata
`name`.

## JoinNode

`JoinNode(PlanNode Left, PlanNode Right, JoinKind Kind, ParsedExpression On)` combines two source
subtrees. The join predicate is parsed as `ExpressionKind.Predicate` against the merged alias-nested row.
A two-source request looks like this:

```json
{
  "sources": [
    { "alias": "b", "name": "buyers" },
    { "alias": "p", "name": "purchases" }
  ],
  "joins": [
    {
      "left": "b",
      "right": "p",
      "kind": "inner",
      "on": { "source": "b.id == p.buyer_id", "language": "cel" }
    }
  ]
}
```

Validation rules:

| Rejection                                            | Reason             |
| ---------------------------------------------------- | ------------------ |
| `JoinKind.Unspecified`                               | `INVALID_ARGUMENT` |
| unknown join alias                                   | `INVALID_ARGUMENT` |
| joining aliases that are already in the same subtree | `INVALID_ARGUMENT` |
| unconnected sources after all joins fold             | `INVALID_ARGUMENT` |

## FilterNode

`FilterNode(Input, Predicate)` comes from `TransformationSpec.Filter`. The predicate slot is parsed as
`ExpressionKind.Predicate`:

```json
{ "filter": { "predicate": { "source": "age > 20" } } }
```

A single-source filter can be pushed into a driver when that driver and expression pushdown planner can
lower it. Multi-source filters run locally over alias-nested dictionaries.

## ComputeNode

`ComputeNode(Input, Fields)` comes from `TransformationSpec.Compute`. Each `ComputedFieldSpec` parses
its expression as `ExpressionKind.Value` and stores the result under `Alias`:

```json
{
  "compute": {
    "fields": [
      {
        "alias": "taxed",
        "expression": { "source": "double(s.age) + 1", "language": "cel" }
      }
    ]
  }
}
```

Value slots require an `ExpressionLanguageDescriptor` for the resolved language with `SupportsValues`.
A missing or non-value-capable descriptor throws `EXPRESSION_LANGUAGE_NOT_VALUE_CAPABLE`.

## GroupNode

`GroupNode(Input, Keys, Aggregations)` comes from `TransformationSpec.GroupBy`. Keys are string paths,
and each `AggregationSpec` becomes an `Aggregation(alias, function, field)`:

```json
{
  "group_by": {
    "keys": ["p.status"],
    "aggregations": [
      { "field": "p.amount", "function": "sum", "alias": "paid_total" }
    ]
  }
}
```

The local executor groups by resolved key values, writes group keys under their last path segment, and
supports `Sum`, `Avg`, `Min`, `Max`, `Count`, and `CountDistinct`.

## OrderNode

`OrderNode(Input, OrderBy)` comes from `TransformationSpec.OrderBy`. Order syntax is parsed by
`IOrderCompiler`, not by the request expression language:

```json
{ "order_by": { "order_by": "age desc" } }
```

Invalid order syntax is reported as `INVALID_ARGUMENT`.

## LimitNode

`LimitNode(Input, Skip, Take)` has two roles:

| Location                  | Source                                    | Behavior                                                     |
| ------------------------- | ----------------------------------------- | ------------------------------------------------------------ |
| top-level root            | `QueryInsightRequest.Skip` and `PageSize` | always added by `BuildAsync`                                 |
| nested selection pipeline | `TopTransform` and `SkipTransform`        | allowed inside a nested `SelectionSpec.Transformations` list |

Top-level `TopTransform` and `SkipTransform` are rejected with `UNIMPLEMENTED`. Use request paging
fields instead.

## SelectionNode

`SelectionNode(Input, Items)` projects the final row shape. An empty `Selections` list means every
field from the source row is returned. A flat field selection records `SelectionKind.Field` and the
field path:

```json
{ "field": "s.full_name", "alias": "full_name" }
```

A computed selection records `SelectionKind.Expression` and must provide an alias:

```json
{
  "expression": { "source": "double(s.age) * 2", "language": "cel" },
  "alias": "age_score"
}
```

A flat selection with local transformations is rejected because local child transformations only make
sense for nested lists.

## Nested selections

A nested `SelectionSpec` has `Field`, child `Selections`, and optional local child `Transformations`.
The builder creates a child `SourceNode` whose `SourceConfig.Params` copy the parent config and add
`"navigation"` with the final navigation segment:

```json
{
  "field": "c.orders",
  "alias": "recent_paid_orders",
  "transformations": [
    { "filter": { "predicate": { "source": "o.status = 'paid'" } } },
    { "order_by": { "order_by": "o.placed desc" } },
    { "top": { "count": 2 } }
  ],
  "selections": [
    { "field": "o.number", "alias": "number" },
    {
      "expression": { "source": "double(o.amount) * 1.1", "language": "cel" },
      "alias": "total"
    }
  ]
}
```

Child alias selection follows this rule:

1. If a child field has a dotted prefix, that prefix becomes the child alias (`o` above).
2. Otherwise the last segment of the nested field becomes the child alias (`orders`).

`ParentConfig` requires either an explicit source alias in the nested field (`c.orders`) or exactly one
source in the request.

## Expression language resolution

Every `InsightExpression` has source text and an optional language. `InsightPlanBuilder.Parse` resolves
language in this order:

1. `InsightExpression.Language`
2. `QueryInsightRequest.Language`
3. `SchemataInsightOptions.DefaultLanguage`

The resolved language must have a keyed `IExpressionCompiler`. Missing compilers throw
`UNKNOWN_EXPRESSION_LANGUAGE` with metadata `language`. Parser failures from `ExpressionException` or
`ArgumentException` throw `INVALID_EXPRESSION`.

## Predicate vs value slots

| Slot                                | Kind        |
| ----------------------------------- | ----------- |
| `FilterTransform.Predicate`         | `Predicate` |
| `JoinSpec.On`                       | `Predicate` |
| `ComputedFieldSpec.Expression`      | `Value`     |
| computed `SelectionSpec.Expression` | `Value`     |

Predicate slots compile to boolean tests. Value slots compile to scalar values and require a
value-capable language descriptor.

## Validation reasons

| Reason                                  | Raised when                                                                                    |
| --------------------------------------- | ---------------------------------------------------------------------------------------------- |
| `UNKNOWN_SOURCE_NAME`                   | no catalog resolves a source name, or `RepositoryDriver` cannot resolve a resource collection  |
| `UNKNOWN_EXPRESSION_LANGUAGE`           | no keyed `IExpressionCompiler` exists for the resolved language                                |
| `INVALID_EXPRESSION`                    | expression parsing fails                                                                       |
| `INVALID_ARGUMENT`                      | malformed source aliases, joins, selections, order syntax, page token, or transformation shape |
| `UNIMPLEMENTED`                         | a requested plan shape has no implementation in the current phase                              |
| `EXPRESSION_LANGUAGE_NOT_VALUE_CAPABLE` | a value expression uses a predicate-only language                                              |

## See also

- [Overview](overview.md) — startup and the wire model
- [Drivers](drivers.md) — pushdown and residual execution
- [Transports](transports.md) — error translation at HTTP and gRPC edges
