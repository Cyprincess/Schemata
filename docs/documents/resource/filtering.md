# Filtering

`ResourceOperationHandler.ListAsync` supports AIP-160 filter expressions and AIP-132 order-by expressions. Both are compiled to LINQ expressions at request time and applied to the entity query via `ResourceRequestContainer`. The filter and order compilers are resolved as keyed services from DI; the keys are hard-wired to `AipLanguage.Name` ("aip").

## Where the code lives

| Package | Key files |
|---|---|
| `Schemata.Resource.Foundation` | `ResourceOperationHandler.cs` (lines 193-220) |
| `Schemata.Resource.Foundation` | `ResourceRequestContainer.cs` |
| `Schemata.Expressions.Aip` | `AipCompiler.cs`, `AipOrderCompiler.cs`, `AipLanguage.cs` |
| `Schemata.Expressions.Skeleton` | `IExpressionCompiler.cs`, `IOrderCompiler.cs` |

## Filter parameter

The `filter` query parameter accepts an AIP-160 filter expression. The expression is compiled to an `Expression<Func<TEntity, bool>>` and applied as a `Where` clause on the entity query.

```
GET /students?filter=grade>3 AND name:"alice"
```

## Order-by parameter

The `order_by` query parameter accepts a comma-separated list of field names with optional `ASC` or `DESC` suffixes (default is ascending).

```
GET /students?order_by=grade DESC, name ASC
```

## How compilation works

```csharp
// Filter
var compiler = _sp.GetRequiredKeyedService<IExpressionCompiler>(AipLanguage.Name);
var tree = compiler.Parse(request.Filter);
var filter = compiler.Compile<TEntity, bool>(tree);
container.ApplyFiltering(filter);

// Order
var compiler = _sp.GetRequiredKeyedService<IOrderCompiler>(AipLanguage.Name);
var order = compiler.CompileOrder<TEntity>(request.OrderBy);
container.ApplyOrdering(order);
```

Both compilers are registered as keyed singletons under `AipLanguage.Name` by `services.AddAipExpressions()`, which `SchemataResourceFeature` calls automatically.

## Hard-wired to AIP (caveat #4)

`ListAsync` resolves `IExpressionCompiler` and `IOrderCompiler` by the key `AipLanguage.Name` ("aip"). This key is hard-coded in the handler. Registering a different `IExpressionCompiler` under a custom key (e.g., `"cel"`) does not affect `ListAsync` — the resource system will still use the AIP compiler for filtering and ordering.

If you need CEL or a custom language for filtering, you must call the compiler directly in your own code (e.g., in a custom advisor or a non-resource endpoint). See [Custom Language](../expressions/custom-language.md) for how to register a custom compiler.

## Pagination

Pagination uses a cursor-based page token. The `page_token` parameter is a Brotli-compressed JSON object containing `parent`, `filter`, `order_by`, `show_deleted`, `page_size`, and `skip`, sealed with ASP.NET Core Data Protection so clients can neither read nor alter it; tokens that fail to decode throw `ValidationException` (`invalid_page_token`). The decoded token is validated against the current request parameters — if any of `parent`, `filter`, `order_by`, or `show_deleted` differ from the token, a `ValidationException` is thrown.

| Parameter | Default | Max |
|---|---|---|
| `page_size` | 25 | 100 |
| `skip` | 0 | unbounded |

The query fetches one look-ahead row beyond the page size, and `next_page_token` is included only when that extra row exists - an exactly-full last page omits it. A negative `page_size` throws `ValidationException` (`invalid_page_size`). `total_size` computation is configurable through `TotalSizeMode` (`Exact` by default, `Estimated` via `IRepository.EstimateCountAsync`, or `None` to omit the field and skip counting), set globally on `SchemataResourceOptions.TotalSize` or per resource on `ResourceAttribute.TotalSize`.

## `ResourceRequestContainer`

`ResourceRequestContainer<T>` accumulates query modifications as a composable `Func<IQueryable<T>, IQueryable<T>>`:

| Method | Effect |
|---|---|
| `ApplyFiltering(predicate)` | Appends a `Where(predicate)` clause |
| `ApplyOrdering(order)` | Applies the order function |
| `ApplyPaginating(token)` | Appends `Skip` and `Take` |
| `ApplyModification(predicate)` | Appends an arbitrary `Where` clause (used for parent scoping and entitlement filtering) |

All modifications compose in the order they are applied. The final `Query` function is passed to `repository.CountAsync` and `repository.ListAsync`.

## Supported AIP-160 operators

| Operator | Syntax | Example |
|---|---|---|
| Equality | `=` | `name = "alice"` |
| Inequality | `!=` | `grade != 3` |
| Less than | `<` | `grade < 3` |
| Less than or equal | `<=` | `grade <= 3` |
| Greater than | `>` | `grade > 3` |
| Greater than or equal | `>=` | `grade >= 3` |
| Has (substring/membership) | `:` | `name:"ali"` |
| Wildcard | `*` | `name:*` (presence check) |
| Logical AND | `AND` | `grade > 2 AND name:"ali"` |
| Logical OR | `OR` | `grade = 1 OR grade = 2` |
| Logical NOT | `NOT` | `NOT grade = 3` |
| Negation | `-` | `-grade = 3` |
| Grouping | `(...)` | `(grade > 2 OR name:"ali")` |

For the full grammar and built-in functions (`timestamp(...)`, `duration(...)`), see [AIP Expressions](../expressions/aip.md).

## Error handling

If the filter or order expression fails to parse, `ListAsync` catches `ParseException` and `ArgumentException` and throws a `ValidationException` with:

- `Field`: `"filter"` or `"order_by"` (wire name)
- `Reason`: `FieldReasons.InvalidFilter` or `FieldReasons.InvalidOrderBy`

## Extension points

- Implement `IResourceListRequestAdvisor<TEntity>` to add additional predicates via `container.ApplyModification(predicate)` (e.g., entitlement filtering, tenant scoping).
- The `ResourceRequestContainer` is passed to all list request advisors, so any advisor can add predicates before the query executes.

## Design motivation

Compiling filter expressions to LINQ at request time (rather than evaluating them in memory) lets the database engine apply the filter efficiently. The `ExpressionCache` (see [Expressions Overview](../expressions/overview.md)) caches compiled expressions by a SHA-256 key so repeated identical filters don't re-compile.

## Caveats

- The filter and order compilers are hard-wired to `AipLanguage.Name`. Registering a CEL or custom compiler does not affect `ListAsync`.
- The `has` operator (`:`) compiles to a `Contains` call on strings and a membership check on collections. Its behavior on non-string, non-collection properties is undefined and may throw at runtime.
- Wildcard `*` in a `has` expression compiles to a presence check (not-null, not-empty). It does not support glob patterns.
- `CountAsync` runs before pagination. On large tables, consider caching the count or using approximate counts.

## See also

- [Resource Overview](overview.md)
- [Read Pipeline](read-pipeline.md)
- [AIP Expressions](../expressions/aip.md)
- [Expressions Overview](../expressions/overview.md)
- [Custom Language](../expressions/custom-language.md)
