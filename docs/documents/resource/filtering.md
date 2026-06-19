# Filtering

`ResourceOperationHandler.ListAsync` accepts an AIP-160 filter and an AIP-132 order-by, compiles each to a LINQ
expression at request time, and applies it to the entity query through `ResourceRequestContainer<TEntity>`. The
filter and order compilers are resolved as keyed DI services under the fixed key `AipLanguage.Name` (`"aip"`).

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Resource.Foundation` | `ResourceOperationHandler.List.cs`, `ResourceRequestContainer.cs`, `Models/PageToken.cs` |
| `Schemata.Expressions.Aip` | `AipCompiler.cs`, `AipOrderCompiler.cs`, `AipLanguage.cs` |
| `Schemata.Expressions.Skeleton` | `IExpressionCompiler.cs`, `IOrderCompiler.cs` |

## `filter` parameter

The `filter` query parameter takes an AIP-160 expression compiled to `Expression<Func<TEntity, bool>>` and applied
as a `Where` clause:

```
GET /v1/students?filter=age>18 AND full_name:"ali"
```

## `order_by` parameter

The `order_by` query parameter takes a comma-separated list of fields with optional `ASC` (default) or `DESC`:

```
GET /v1/students?order_by=age DESC, full_name ASC
```

## How compilation works

```csharp
// filter
var compiler = _sp.GetRequiredKeyedService<IExpressionCompiler>(AipLanguage.Name);
var tree     = compiler.Parse(request.Filter);
var filter   = compiler.Compile<TEntity, bool>(tree);
container.ApplyFiltering(filter);

// order
var order = _sp.GetRequiredKeyedService<IOrderCompiler>(AipLanguage.Name)
               .CompileOrder<TEntity>(request.OrderBy);
container.ApplyOrdering(KeyOrdering<TEntity>.Compose(order));
```

Both are registered as keyed singletons by `services.AddAipExpressions()`, which `SchemataResourceFeature` calls
automatically. The key is fixed to `AipLanguage.Name` in the handler; registering an `IExpressionCompiler` under a
different key (e.g. `"cel"`) does not change List behavior. To filter with another language, call its compiler
directly from a custom advisor or endpoint — see [Custom Language](../expressions/custom-language.md).

## Supported AIP-160 operators

| Operator | Syntax | Example |
| --- | --- | --- |
| Equality | `=` | `full_name = "alice"` |
| Inequality | `!=` | `age != 20` |
| Less than | `<` | `age < 25` |
| Less than or equal | `<=` | `age <= 25` |
| Greater than | `>` | `age > 18` |
| Greater than or equal | `>=` | `age >= 18` |
| Has (substring/membership) | `:` | `full_name:"ali"` |
| Wildcard presence | `*` (with `:`) | `tags:*` |
| Logical AND | `AND` | `age > 18 AND full_name:"ali"` |
| Logical OR | `OR` | `age = 1 OR age = 2` |
| Logical NOT | `NOT` / `-` | `NOT age = 3`, `-age = 3` |
| Grouping | `(...)` | `(age > 18 OR full_name:"ali")` |

For the full grammar, value types, and built-in functions (`timestamp`, `duration`), see
[AIP Expressions](../expressions/aip.md).

## Pagination

`PageToken` carries `Parent`, `Filter`, `OrderBy`, `ShowDeleted`, `PageSize`, and `Skip`. It is serialized to JSON,
Brotli-compressed, sealed with ASP.NET Core Data Protection (purpose
`Schemata.Resource.Foundation.PageToken`), and emitted as a URL-safe Base64 string, so a client can neither read
nor forge it. `PageToken.FromStringAsync` rejects a tampered or malformed token with `ValidationException`
(`InvalidPageToken`).

| Parameter | Default | Cap |
| --- | --- | --- |
| `page_size` | 25 | 100 |
| `skip` | 0 | unbounded |

The query fetches one look-ahead row beyond `page_size`; `next_page_token` is emitted only when that extra row
exists, so an exactly-full last page omits it per AIP-158. A negative `page_size` throws `ValidationException`
(`InvalidPageSize`). A decoded token whose `Parent`, `Filter`, `OrderBy`, or `ShowDeleted` differ from the request
throws `ValidationException` (`InvalidPageToken`).

`total_size` follows `TotalSizeMode`: `Exact` (default) counts with `CountAsync`, `Estimated` uses
`EstimateCountAsync`, `None` omits the field and skips counting. Set it globally on
`SchemataResourceOptions.TotalSize` or per resource on `ResourceAttribute.TotalSize`.

## `ResourceRequestContainer`

`ResourceRequestContainer<T>` accumulates query modifications into a composable
`Func<IQueryable<T>, IQueryable<T>>`:

| Method | Effect |
| --- | --- |
| `ApplyFiltering(predicate)` | Appends a `Where(predicate)` |
| `ApplyOrdering(order)` | Applies the order function |
| `ApplyPaginating(token, lookahead)` | Appends `Skip` and `Take` (with the look-ahead row) |
| `ApplyModification(predicate)` | Appends an arbitrary `Where` (parent scoping, entitlement filtering) |

The composed `Query` function is passed to `CountAsync` and `ListAsync`.

## Error mapping

A filter or order that fails to parse raises `ParseException` or `ArgumentException`, which `ListAsync` converts to
`ValidationException` with `Field` `filter` or `order_by` and reason `FieldReasons.InvalidFilter` or
`FieldReasons.InvalidOrderBy`. The HTTP transport surfaces this as `422`; gRPC surfaces it as
`InvalidArgument`.

## Extension points

- Implement `IResourceListRequestAdvisor<TEntity>` to add predicates via `container.ApplyModification`
  (entitlement, tenant scoping). The container is passed to every list request advisor.

## Design rationale

Compiling filters to LINQ (rather than evaluating in memory) lets the database apply them. The shared
`ExpressionCache` keys compiled expressions by a SHA-256 hash so a repeated identical filter does not recompile —
see [Expressions Overview](../expressions/overview.md).

## Caveats

- The filter and order compilers are fixed to `AipLanguage.Name`.
- The `has` operator (`:`) compiles to a `Contains` call on strings and a membership check on collections;
  `field:*` is a presence check, not a glob pattern.
- `CountAsync` runs before paging; on large tables use `TotalSizeMode.Estimated` or `None`.

## See also

- [Read Pipeline](read-pipeline.md)
- [AIP Expressions](../expressions/aip.md)
- [Expressions Overview](../expressions/overview.md)
- [Filtering and Pagination](../../guides/filtering-and-pagination.md)
