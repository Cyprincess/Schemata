# Drivers

A source driver is the boundary between Insight's logical plan and a backend. It receives one
single-source `SubPlan`, lowers the parts it understands into the backend, streams string-keyed rows,
and returns a schema for those rows. `PlanExecutor` runs the stages the driver cannot own in the local
pipeline.

## ISourceDriver

```csharp
public interface ISourceDriver
{
    string Name { get; }

    DriverCapabilities Capabilities { get; }

    ValueTask<ISourceResult> ExecuteAsync(
        SubPlan             subPlan,
        QueryInsightRequest request,
        ClaimsPrincipal?    principal,
        CancellationToken   ct = default);
}
```

`Name` is the keyed DI name used by `AddSourceDriver<TDriver>(name)` and by `SourceConfig.DriverName`.
`Capabilities` advertises the operators the driver can lower. `ExecuteAsync` receives the original
request and principal because source-level security belongs inside the driver after it resolves the row
type.

## ISourceResult

```csharp
public interface ISourceResult : IAsyncDisposable
{
    IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Rows { get; }

    IReadOnlyList<FieldDescriptor> Schema { get; }
}
```

Rows use snake_case string keys. A driver may stream flat rows for a single source. When local stages
run, `LocalPipelineExecutor` wraps those rows under the source alias before evaluating expressions.

## DriverCapabilities

| Flag | Meaning |
| --- | --- |
| `Filter` | driver can apply `FilterNode` |
| `Compute` | driver can apply `ComputeNode` |
| `Project` | driver can apply `SelectionNode` fields |
| `Order` | driver can apply `OrderNode` |
| `Group` | driver can apply `GroupNode` |
| `Limit` | driver can apply `LimitNode` |
| `Join` | driver can join sources in the same backend |
| `Nested` | driver can own nested list projection |

`DriverCapabilities.All` is the union of all flags. The current splitter uses the plan shape and the
built-in barriers described below; custom drivers should still report accurate flags because the flags
are part of the public contract.

## Pushable and local stages

`PlanExecutor.Split` walks the single-source chain from the root toward `SourceNode`. A plan is handed
to the driver unchanged until a local barrier appears. The current barriers are:

- `ComputeNode`
- `GroupNode`
- a `SelectionNode` containing nested or computed selections

When a barrier exists, only the source plus contiguous filter/order stages below the barrier are pushed.
The barrier and every stage above it run locally. A nested selection also appears in the pushable side so
the repository driver can materialize child lists, then it runs locally for child pipelines and final
projection.

## Local pipeline

`LocalPipelineExecutor` works over alias-nested dictionaries:

```text
{ "s": { "full_name": "Ada", "age": 36 } }
```

It supports filter, compute, group, order, limit, selection, and join stages. Computed values and group
aggregates become root scalar keys. Terminal selection flattens the response shape.

Joins are local nested-loop joins over compiled predicates. The buffered side is capped by
`SchemataInsightOptions.MaxResidualScanRows` (default 10,000).

## RepositoryDriver

`RepositoryDriver` is the built-in driver for Schemata repositories. Sources register it with the keyed
name `RepositoryDriver.DriverName`, whose value is `"repository"`:

```csharp
schema.UseInsight(i => {
    i.AddRepositorySource("students", "students")
     .AddSourceDriver<RepositoryDriver>(RepositoryDriver.DriverName);
});
```

`AddRepositorySource(name, resource)` stores `Params["resource"] = resource`. At execution time the
driver resolves the resource collection back to an entity type by scanning `ICanonicalName` types and
comparing `ResourceNameDescriptor.ForType(type).Collection`.

### Capabilities

`RepositoryDriver.Capabilities` includes:

- `Filter`
- `Compute`
- `Project`
- `Order`
- `Group`
- `Limit`

It does not report `Join` because joins can span heterogeneous repository providers. It does not report
`Nested`, although it can eager-load navigation collections and pass child rows into the local nested
selection pipeline.

### Query lowering

For each source, `RepositoryDriver` builds an `IQueryable<TEntity>` callback for
`IRepository<TEntity>.ListAsync`:

1. `InsightSecurityGate.AuthorizeAsync` returns a row entitlement expression, when one is registered.
2. The entitlement expression is applied with `Where`.
3. Each pushed `FilterNode` is planned through the keyed `IExpressionPushdownPlanner`.
4. The pushed part is compiled through the keyed `IExpressionCompiler` and applied to the query.
5. The residual part is compiled to `Func<TEntity, bool>` and stored for post-query filtering.
6. `OrderNode` is compiled through `IOrderCompiler.CompileOrder<TEntity>`.
7. Nested selections are converted to PascalCase navigation names and passed through EF Core `Include`
   by reflection when the extension method is available.

The driver then streams repository entities, applies every residual predicate, and calls
`RowMaterializer.ToRow`.

### Nested selections and EF Include

Nested selections need the parent navigation collection loaded before the local child pipeline runs.
`RepositoryDriver.NavigationNames` strips the parent alias and Pascalizes each segment:

```text
c.orders -> Orders
c.orders.items -> Orders.Items
```

`Include(query, navigation)` binds `Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions`
reflectively. EF Core providers receive the string include. Providers without that extension continue
without include support, so navigation loading follows the provider's own behavior.

`RowMaterializer` converts each child object in the navigation collection into a snake_case dictionary.
The nested local pipeline then filters, orders, limits, computes, groups, and projects those child rows.

### Schema materialization

`SchemaBuilder.For` maps selected entity properties to `FieldDescriptor` values:

| CLR type | FieldType |
| --- | --- |
| `string`, `Guid` | `String` |
| integral types | `Int64` |
| `float`, `double`, `decimal` | `Double` |
| `bool` | `Bool` |
| `DateTime`, `DateTimeOffset` | `Timestamp` |
| `TimeSpan` | `Duration` |
| `byte[]` | `Bytes` |
| other types | `Object` |

Computed selections currently report `FieldType.Object` because the expression result type is dynamic.

## Source-level security

`RepositoryDriver` calls `InsightSecurityGate.AuthorizeAsync(typeof(TEntity), request, principal, …)`
before the repository query runs. The gate resolves Security providers for the entity type and
`QueryInsightRequest`:

```csharp
IAccessProvider<TEntity, QueryInsightRequest>
IEntitlementProvider<TEntity, QueryInsightRequest>
```

An access provider can reject the whole source. An entitlement provider can return an expression that
becomes part of the backend query.

## Author a custom driver

1. Choose a stable driver name.
2. Implement `ISourceDriver` and return the operators your backend can lower through `Capabilities`.
3. Validate required `SourceConfig.Params` values at the start of `ExecuteAsync`.
4. Run source access checks before opening the backend connection.
5. Lower the `SubPlan.Root` stages your driver accepts.
6. Return rows with string keys and a `FieldDescriptor` schema.
7. Register the driver and source bindings:

```csharp
schema.UseInsight(i => {
    i.AddSource("warehouse_orders", "warehouse", new Dictionary<string, object?> {
        ["view"] = "sales.orders"
    });
    i.AddSourceDriver<WarehouseInsightDriver>("warehouse");
});
```

A custom driver can reject unsupported nodes with `InsightValidationException` and reason
`UNIMPLEMENTED`. Use `INVALID_ARGUMENT` for malformed source parameters.

## See also

- [Overview](overview.md) — catalog, driver, and security model
- [Planning](planning.md) — plan nodes and validation rules
- [Transports](transports.md) — how driver errors surface over HTTP and gRPC
