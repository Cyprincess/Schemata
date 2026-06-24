# Filtering

`ResourceOperationHandler.ListAsync` accepts a filter, language, order-by, pagination settings, and parent scope. It resolves the filter language from the resource module profile, compiles the filter through the selected expression compiler, and applies ordering through the language-independent order compiler.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Resource.Foundation` | `ResourceOperationHandler.List.cs`, `ResourceRequestContainer.cs`, `Models/PageToken.cs`, `SchemataResourceOptions.cs`, `SchemataResourceBuilder.cs` |
| `Schemata.Expressions.Skeleton` | `ExpressionLanguageResolver.cs`, `ResolvedLanguage.cs`, `FilteringMode.cs`, `ResidualPage.cs`, `IExpressionCompiler.cs`, `IExpressionPushdownPlanner.cs`, `IOrderCompiler.cs` |
| `Schemata.Expressions.Aip` | `AipCompiler.cs`, `AipPushdownPlanner.cs`, `ExpressionLanguageBuilderExtensions.cs` |
| `Schemata.Expressions.Cel` | `CelCompiler.cs`, `CelPushdownPlanner.cs`, `ExpressionLanguageBuilderExtensions.cs` |
| `Schemata.Expressions.Order` | `OrderCompiler.cs`, `ServiceCollectionExtensions.cs` |

## Enabling languages

`SchemataResourceBuilder` implements `IExpressionLanguageBuilder`. Enable filter languages and ordering on that builder:

```csharp
builder.UseSchemata(schema => {
    schema.UseResource()
          .UseAip(entry => entry.Filtering = FilteringMode.Residual)
          .UseCel()
          .UseOrdering();
});
```

The builder stores its `ExpressionLanguageProfile` into `SchemataResourceOptions.Expressions` when at least one language is enabled. The first enabled language is the default for requests that omit `language`; explicit `language` values must name an enabled entry.

## `filter` and `language`

A request can omit `language` or select an enabled language:

```text
GET /v1/students?filter=age>18 AND full_name:"ali"
GET /v1/students?language=cel&filter=age > 18 && full_name.contains('ali')
```

The handler resolves the request like this:

```csharp
var resolved = ExpressionLanguageResolver.Resolve(
    profile,
    request.Language,
    name => _sp.GetKeyedService<ExpressionLanguageDescriptor>(name));

var compiler = _sp.GetRequiredKeyedService<IExpressionCompiler>(resolved.Language);
var tree = compiler.Parse(request.Filter);
```

If the profile is empty or the requested language is not enabled, the handler maps `UnknownExpressionLanguageException` to a validation error on `language`.

## Filtering mode and residual evaluation

The effective `FilteringMode` comes from descriptor, profile, and entry settings. `Strict` at any level wins; `Residual` applies only when no level is strict; an all-default chain becomes `Strict`.

In `Strict`, the handler compiles the full tree and applies it to the repository query:

```csharp
container.ApplyFiltering(compiler.Compile<TEntity, bool>(tree));
```

In `Residual`, the handler resolves `IExpressionPushdownPlanner` keyed by the selected language and plans against `ExpressionCapabilities.Relational`:

```csharp
var planner = _sp.GetRequiredKeyedService<IExpressionPushdownPlanner>(resolved.Language);
var plan = planner.Plan(tree, ExpressionCapabilities.Relational);

if (plan.Pushed is not null) {
    container.ApplyFiltering(compiler.Compile<TEntity, bool>(plan.Pushed));
}

if (plan.Residual is not null) {
    residual = compiler.Compile<TEntity, bool>(plan.Residual).Compile();
}
```

The pushed tree narrows the backend query to a superset. `ResidualPage.ScanAsync` then evaluates the residual locally, applies `skip` after residual matches, collects the page, and detects `HasMore`. The default residual scan cap is `10_000` source rows. Override it at descriptor level through `AddAipExpressions` / `AddCelExpressions`, at module profile level, or at language entry level:

```csharp
schema.UseResource()
      .UseAip(entry => {
          entry.Filtering = FilteringMode.Residual;
          entry.MaxResidualScanRows = 20_000;
      });
```

Reaching the cap before the page or exact count is known throws `InvalidOperationException`.

## `order_by`

`order_by` uses the non-keyed `IOrderCompiler` from `Schemata.Expressions.Order`:

```csharp
var compiler = _sp.GetRequiredService<IOrderCompiler>();
var order = compiler.CompileOrder<TEntity>(request.OrderBy);
container.ApplyOrdering(KeyOrdering<TEntity>.Compose(order));
```

The syntax is AIP-132: comma-separated fields with optional `asc` or `desc` direction.

```text
GET /v1/students?order_by=age desc, full_name asc
```

Enable it with `UseOrdering()` or `services.AddOrderExpressions()`.

## Supported AIP-160 operators

AIP remains the default language when `UseAip()` is the first enabled entry. Its common operators are:

| Operator | Syntax | Example |
| --- | --- | --- |
| Equality | `=` | `full_name = "alice"` |
| Inequality | `!=` | `age != 20` |
| Less than | `<` | `age < 25` |
| Less than or equal | `<=` | `age <= 25` |
| Greater than | `>` | `age > 18` |
| Greater than or equal | `>=` | `age >= 18` |
| Has | `:` | `full_name:"ali"` |
| Presence | `:*` | `tags:*` |
| Logical AND | `AND` | `age > 18 AND full_name:"ali"` |
| Logical OR | `OR` | `age = 1 OR age = 2` |
| Logical NOT | `NOT` / `-` | `NOT age = 3`, `-age = 3` |
| Grouping | `(...)` | `(age > 18 OR full_name:"ali")` |

For CEL syntax, see [CEL Expressions](../expressions/cel.md). For the full AIP grammar, see [AIP Expressions](../expressions/aip.md).

## Pagination

`PageToken` carries `Parent`, `Filter`, `Language`, `OrderBy`, `ShowDeleted`, `PageSize`, and `Skip`. It is serialized to JSON, Brotli-compressed, sealed with ASP.NET Core Data Protection purpose `Schemata.Resource.Foundation.PageToken`, and emitted as URL-safe Base64. `PageToken.FromStringAsync` rejects tampered or malformed tokens with `ValidationException` (`InvalidPageToken`).

| Parameter | Default | Cap |
| --- | --- | --- |
| `page_size` | 25 | 100 |
| `skip` | 0 | unbounded |

The token must match `Parent`, `Filter`, `Language`, `OrderBy`, and `ShowDeleted` from the current request. A mismatch throws `ValidationException` (`InvalidPageToken`). A negative `page_size` throws `ValidationException` (`InvalidPageSize`).

Without a residual predicate, paging applies in the backend query with one look-ahead row. With a residual predicate, backend paging is delayed until `ResidualPage` has applied local filtering.

`total_size` follows `TotalSizeMode`: `Exact` (default) counts exactly, `Estimated` calls `EstimateCountAsync`, and `None` omits the field. During residual evaluation, exact mode uses `ResidualPage`'s exact total; estimated mode estimates the pushed superset.

## `ResourceRequestContainer`

`ResourceRequestContainer<T>` accumulates query modifications into a composable `Func<IQueryable<T>, IQueryable<T>>`:

| Method | Effect |
| --- | --- |
| `ApplyFiltering(predicate)` | Appends `Where(predicate)`. |
| `ApplyOrdering(order)` | Applies the order function. |
| `ApplyPaginating(token, lookahead)` | Appends `Skip` and `Take` with the look-ahead row. |
| `ApplyModification(predicate)` | Appends an arbitrary `Where` for advisors. |

The composed `Query` function is passed to `CountAsync`, `EstimateCountAsync`, and `ListAsync`.

## Error mapping

A malformed filter, unknown field, or failed compilation maps to `ValidationException` with field `filter` and reason `FieldReasons.InvalidFilter`. An unknown language maps to field `language` and reason `FieldReasons.InvalidFilter`. An invalid order-by maps to field `order_by` and reason `FieldReasons.InvalidOrderBy`.

HTTP surfaces these as `422`; gRPC surfaces them as `InvalidArgument`.

## Extension points

- Implement `IResourceListRequestAdvisor<TEntity>` to add predicates through `container.ApplyModification` before the handler compiles the request filter.
- Implement a custom `IExpressionCompiler` plus `Use*` builder seam to add a filter language.
- Implement `IExpressionPushdownPlanner` for residual mode when the language can push a backend-safe subset.

## Caveats

- `field:*` is a presence check, not a glob pattern.
- Residual mode can scan up to `MaxResidualScanRows` source rows per request before failing.
- `CountAsync` runs before paging in strict mode; use `TotalSizeMode.Estimated` or `None` on large collections when exact totals are not needed.

## See also

- [Read Pipeline](read-pipeline.md)
- [Expressions Overview](../expressions/overview.md)
- [Pushdown and Residual Evaluation](../expressions/pushdown.md)
- [AIP Expressions](../expressions/aip.md)
- [CEL Expressions](../expressions/cel.md)
- [Filtering and Pagination](../../guides/filtering-and-pagination.md)
