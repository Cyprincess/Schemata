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

| Flag      | Meaning                                     |
| --------- | ------------------------------------------- |
| `Filter`  | driver can apply `FilterNode`               |
| `Compute` | driver can apply `ComputeNode`              |
| `Project` | driver can apply `SelectionNode` fields     |
| `Order`   | driver can apply `OrderNode`                |
| `Group`   | driver can apply `GroupNode`                |
| `Limit`   | driver can apply `LimitNode`                |
| `Join`    | driver can join sources in the same backend; the current executor evaluates joins locally |
| `Nested`  | driver can materialize child collections for local nested projection |

`DriverCapabilities.All` is the union of all flags. The current splitter uses the plan shape and the
built-in barriers described below; custom drivers should still report accurate flags because the flags
are part of the public contract.

## Pushable and local stages

`PlanExecutor.Split` walks the single-source chain from the root toward `SourceNode`. It leaves a
top-level `LimitNode` local and stops before any other stage the driver does not advertise. Computed
selections run locally. A selection containing nested items also runs locally so the child pipeline can
filter, order, limit, compute, group, and project its collection.

When a driver advertises `Nested`, the splitter also sends a nested-only selection in the pushed prefix
so the driver can materialize the child collection. A driver without `Nested` leaves the terminal
selection local and supplies a child collection in its raw parent rows when it supports nested data.

## Local pipeline

`LocalPipelineExecutor` works over alias-nested dictionaries:

```text
{ "s": { "full_name": "Ada", "age": 36 } }
```

It supports filter, compute, group, order, limit, selection, and join stages. Computed values and group
aggregates become root scalar keys. Terminal selection flattens the response shape.

Local `Min` and `Max` use one comparer. Values of the same CLR type use `IComparable`; mixed numeric
types are converted through `double` before comparison.

Joins are local nested-loop joins over compiled predicates. The buffered side is capped by
`SchemataInsightOptions.MaxResidualScanRows` (default 10,000).

## RepositoryDriver

`RepositoryDriver` is the built-in driver for Schemata repositories. Sources register it with the keyed
name `RepositoryDriver.DriverName`, whose value is `"repository"`:

```csharp
using Schemata.Insight.Foundation.Drivers;

schema.UseInsight(i => {
    i.AddRepositorySource<Student, StudentRow>("students",
            s => new StudentRow { FullName = s.FullName, Age = s.Age })
     .AddSourceDriver<RepositoryDriver>(RepositoryDriver.DriverName);
});

public sealed class StudentRow
{
    public string? FullName { get; set; }
    public int     Age      { get; set; }
}
```

`AddRepositorySource<TEntity, TPublic>(name, projection)` registers a keyed
`RepositorySource<TEntity, TPublic>` under `name` and stores `Params["binding"] = name`. At execution
time the driver resolves the binding back to the closed `RepositorySource<TEntity, TPublic>` through
`GetKeyedService<RepositorySource>(binding)` rather than scanning `ICanonicalName` types or matching
`ResourceNameDescriptor.ForType(type).Collection` against a string. Each source's `TPublic`
properties are the field set the public-shape validator, filter pushdown, ordering, and row
materializer see. `TEntity` still backs the repository query and entitlement check.

### Capabilities

`RepositoryDriver.Capabilities` includes:

- `Filter`
- `Project`
- `Order`
- `Nested`

It does not report `Compute`, `Group`, `Limit`, or `Join`; those stages can span heterogeneous
providers or need local execution. The supplied projection selects navigation collections into
declared public child models for the local nested selection pipeline.

### Query lowering

For each source, the closed repository binding supplies a projected query to
`IRepository<TEntity>.ListAsync<TPublic>`:

1. `InsightSecurityGate.AuthorizeAsync<TEntity>` obtains entity row entitlement.
2. The entity query applies entitlement with `Where`, then the registered `Select` projection.
3. Pushed filters compile against `TPublic` and remain in the backend query.
4. Residual filters compile against the validated public model and run on projected rows.
5. Ordering compiles against `TPublic`; the projection supplies nested navigation data.
6. `RowMaterializer` recursively converts only declared public members into rows.

### Nested selections

A nested `SelectionSpec` produces child rows from the public shape, not from the entity navigation
graph. The projection supplies the child collection: the `TPublic` row must project navigation
properties into a collection of public-shaped children (or another `TPublic`-shaped value):

```csharp
public sealed class CustomerRow
{
    public string?             FullName { get; set; }
    public List<OrderRow>?     Orders   { get; set; }
}

public sealed class OrderRow
{
    public int     Number { get; set; }
    public string? Status { get; set; }
    public int     Amount { get; set; }
    public int     Placed { get; set; }
}
```

`RepositorySource<TEntity, TPublic>` supplies the registered projection to
`IRepository<TEntity>.ListAsync<TPublic>`. `RowMaterializer` converts the declared public child
collection into snake_case dictionaries before local evaluation. A present null collection becomes
an empty list; an absent collection is an error, and a non-collection value raises `INVALID_ARGUMENT`.
Ordinary map-valued fields support key access, but a map is not itself a nested collection selection.
Custom drivers may supply CLR child values; repository-backed rows remain restricted to their
registered declared public types throughout nested evaluation.

### Schema materialization

`SchemaBuilder.For` maps selected public properties to `FieldDescriptor` values:

| CLR type                     | FieldType   |
| ---------------------------- | ----------- |
| `string`, `Guid`             | `String`    |
| integral types               | `Int64`     |
| `float`, `double`, `decimal` | `Double`    |
| `bool`                       | `Bool`      |
| `DateTime`, `DateTimeOffset` | `Timestamp` |
| `TimeSpan`                   | `Duration`  |
| `byte[]`                     | `Bytes`     |
| other types                  | `Object`    |

Computed selections currently report `FieldType.Object` because the expression result type is dynamic.

### Multi-source schema

Multi-source plans run their joins and terminal selection locally. `PlanExecutor` rebuilds the response
schema from that local selection output rather than returning a driver schema, preserving selected
field aliases, computed fields, and nested descriptors.

## Public shape contract

`PublicModel` reads the public type once per CLR type and caches the field map under the property's
snake_case name. The constructor walks `BindingFlags.Instance | BindingFlags.Public` properties and
keeps every property whose public getter is present, that has no index parameters, and that does not
carry a `JsonIgnoreAttribute`:

```csharp
_properties = properties.Where(p => p.GetMethod?.IsPublic == true && p.GetIndexParameters().Length == 0
        && p.GetCustomAttribute<JsonIgnoreAttribute>() is null)
    .ToDictionary(p => p.Name.Underscore(), StringComparer.Ordinal);
```

Properties outside that set — `[JsonIgnore]` members, write-only properties, properties whose getter
is not public, and indexers — are not declared as fields and cannot be referenced by `Field`,
`Filter`, `Order`, `Group`, or `Selection` paths. The declared-name set still comes from every public
property and field so the validator can recognise both the property's PascalCase name and the
underscored wire name.

The public type appears as the row type in every query path that the driver exposes:

| Path | Public-type member used |
| --- | --- |
| Filter pushdown | `IExpressionCompiler.Compile<TPublic, bool>` against the projected row |
| Filter residual | `Func<TPublic, bool>` evaluated after materialization |
| Order compiler | `IOrderCompiler.CompileOrder<TPublic>` against the projected row |
| Nested push | `SchemaBuilder.For(typeof(TPublic), …)` and `RowMaterializer.ToRow` over projected rows |
| Public-shape validation | `PublicModel.For(typeof(TPublic)).Declares(name)` for every referenced path |
| Schema output | `SchemaBuilder.For(typeof(TPublic), …)` selecting properties from `TPublic` |

A reference to a property that exists on `TEntity` but is not projected into `TPublic` fails
validation with reason `INVALID_ARGUMENT`. Reference a property on `TPublic` (or extend the
projection) instead of switching back to the entity type.

### Expression reference providers

`PublicPlanValidator` resolves the public shape of every source in the plan and walks each
expression node to confirm every referenced path is declared on a public type. The walker delegates
to the keyed `IExpressionReferenceProvider`:

```csharp
public interface IExpressionReferenceProvider
{
    ExpressionReferences Analyze(IExpressionTree tree);
}

public sealed record ExpressionReferences(IReadOnlyList<ExpressionReference> References, ExpressionShape Result);
```

`References` enumerates the source-rooted accesses the expression reads; each `ExpressionReference`
carries the dotted path, whether it is dynamic, whether it may be a literal, and whether the path is
repeated. `Result` carries the structural provenance of the expression's value through the
`ExpressionShape` hierarchy (`Scalar`, `Null`, `MapValues`, `Reference`, `Sequence`, `Map`,
`Alternatives`).

`PublicPlanValidator.Analyze` resolves the provider through
`GetKeyedService<IExpressionReferenceProvider>(expression.Language)` and rejects languages that do
not provide one. The validator then walks `analysis.References` to confirm every path resolves
against the source's public shape, and binds `analysis.Result` to the plan environment so the
computed selection can use the expression's structural shape.

Built-in reference providers:

| Language | Type | Returns |
| --- | --- | --- |
| `aip` | `AipReferenceProvider` | `ExpressionReferences(references, Scalar)` |
| `cel` | `CelReferenceProvider` | `ExpressionReferences(references, result)` where `result` is the structural provenance computed by visiting the CEL node |

A custom language registers a reference provider alongside its compiler and pushdown planner so the
public-shape validator can run against the language's expressions.

## Source-level security

`RepositoryDriver` calls `InsightSecurityGate.AuthorizeAsync<TEntity>(request, principal, …)`
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
