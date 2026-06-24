# Insight

Insight is a federated read-query subsystem. A client sends one `QueryInsightRequest` with named
sources, joins, transformations, selections, and pagination metadata. The service resolves each source
through a catalog, builds a logical plan, pushes the single-source prefix into a driver, and runs the
remaining stages in the local dictionary-row pipeline. HTTP and gRPC transports expose the same query
surface.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Insight.Skeleton` | `IInsightService.cs`, `Wire/QueryInsightRequest.cs`, `Wire/QueryInsightResponse.cs`, `Wire/SelectionSpec.cs`, `Wire/Transformations.cs`, `Drivers/ISourceDriver.cs`, `Drivers/DriverCapabilities.cs`, `Catalog/IInsightSourceCatalog.cs`, `Catalog/SourceConfig.cs`, `Entities/SchemataInsightSource.cs`, `Plan/*.cs` |
| `Schemata.Insight.Foundation` | `SchemataInsightBuilder.cs`, `SchemataInsightOptions.cs`, `Features/SchemataInsightFeature.cs`, `Planning/InsightPlanBuilder.cs`, `Execution/DefaultInsightService.cs`, `Execution/PlanExecutor.cs`, `Execution/LocalPipelineExecutor*.cs`, `Drivers/RepositoryDriver.cs`, `Catalog/InMemoryInsightSourceCatalog.cs`, `Catalog/DatabaseInsightSourceCatalog.cs`, `Security/InsightSecurityGate.cs` |
| `Schemata.Insight.Http` | `InsightController.cs`, `Features/SchemataInsightHttpFeature.cs`, `Extensions/SchemataInsightBuilderExtensions.cs` |
| `Schemata.Insight.Grpc` | `IInsightGrpcService.cs`, `InsightGrpcService.cs`, `InsightServiceMethodProvider.cs`, `InsightGrpcMethods.cs`, `Mapping/InsightStructMapper.cs`, `Wire/*.cs`, `Features/SchemataInsightGrpcFeature.cs` |

## Startup

`UseInsight()` on `SchemataBuilder` activates
`Schemata.Insight.Foundation.Features.SchemataInsightFeature` (Priority
`Orders.Extension + 95_000_000` = 495,000,000) and returns a `SchemataInsightBuilder`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Resource;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Expressions.Aip;
using Schemata.Expressions.Cel;
using Schemata.Expressions.Order;
using Schemata.Insight.Foundation;

builder.UseSchemata(schema => {
    var insight = schema.UseInsight(i => {
        i.WithTotalSize(TotalSizeMode.Exact);
        i.AddRepositorySource("students", "students")
         .AddSourceDriver<RepositoryDriver>(RepositoryDriver.DriverName);
    });

    insight.UseAip().UseCel().UseOrdering();
    insight.MapHttp();

    schema.Services.AddDbContextFactory<AppDbContext>(opts => opts.UseSqlite(connectionString));
    schema.Services.AddRepository<Student, EfCoreRepository<AppDbContext, Student>>();
});
```

`SchemataInsightFeature.ConfigureServices` registers:

1. `InMemoryInsightSourceCatalog` as an `IInsightSourceCatalog` singleton.
2. `InsightPlanBuilder`, `LocalPipelineExecutor`, and `PlanExecutor` as singletons.
3. `DefaultInsightService` as `IInsightService` (singleton, `TryAdd`).

The feature registers the in-memory catalog from `SchemataInsightOptions.Sources`. Calling
`UseDatabaseCatalog()` adds `DatabaseInsightSourceCatalog` before the in-memory catalog, so runtime rows
in `SchemataInsightSource` can resolve names and builder-registered names remain as fallback.

## SchemataInsightBuilder

| Member | Effect |
| --- | --- |
| `DefaultLanguage(string language)` | Sets the expression language used when a slot and the request omit `language`; default is `ExpressionLanguages.Aip`. |
| `WithTotalSize(TotalSizeMode mode)` | Sets the `total_size` behavior. `Default` is treated as `Exact`. |
| `AddSource(string name, string driver, IReadOnlyDictionary<string, object?>? parameters = null)` | Adds an in-memory source binding. |
| `AddRepositorySource(string name, string resource)` | Adds a source served by `RepositoryDriver` with the `resource` parameter. |
| `AddSourceDriver<TDriver>(string name)` | Registers a keyed singleton `ISourceDriver`. |
| `UseDatabaseCatalog()` | Resolves source names through `IRepository<SchemataInsightSource>` before the in-memory catalog. |
| `AddFeature<T>()` | Adds a feature to the Schemata configuration. |

Insight also implements `IExpressionLanguageBuilder`, so expression packages can attach their languages
to the same builder with `UseAip()`, `UseCel()`, and `UseOrdering()`.

## Request and response wire types

`QueryInsightRequest` carries the query graph:

| Field | Meaning |
| --- | --- |
| `Sources` | `SourceBinding(alias, name)` entries. `name` is catalog-facing; `alias` is request-facing. |
| `Joins` | `JoinSpec(left, right, kind, on)` entries over source aliases. |
| `Transformations` | Ordered `Filter`, `Compute`, `GroupBy`, `OrderBy`, `Top`, or `Skip` operations. |
| `Selections` | GraphQL-style `SelectionSpec` items for fields, computed expressions, and nested child lists. |
| `PageSize`, `Skip`, `PageToken` | Top-level paging controls. |
| `Language` | Request-level default for expression slots. |

`QueryInsightResponse` returns:

| Field | Meaning |
| --- | --- |
| `Rows` | Nested string-keyed row dictionaries. |
| `Schema` | `FieldDescriptor` tree for response fields. |
| `NextPageToken` | Opaque continuation token, currently an encoded skip offset. |
| `TotalSize` | Exact or estimated count, depending on `SchemataInsightOptions.TotalSize`. |
| `Unreachable` | Source names that could not be reached; reserved for AIP-217 partial responses. |

## Catalog and driver model

`IInsightSourceCatalog` hides backend details from the caller. A catalog resolves a source name to a
`SourceConfig`, which contains a driver name and driver-specific parameters. The built-in repository
source stores the resource collection in `Params["resource"]`.

`ISourceDriver` lowers one `SubPlan` into its backend. The driver advertises its pushdown surface with
`DriverCapabilities`; `PlanExecutor` splits a single-source plan at the first local barrier. The
pushable prefix goes to the driver, and the residual stages run through `LocalPipelineExecutor`.

The built-in `RepositoryDriver` resolves the resource collection to an entity type with
`ResourceNameDescriptor`, reads through `IRepository<TEntity>`, pushes filter and order stages when the
expression planner can lower them, applies residual filters in memory, and materializes rows and schema
from the selected entity properties.

## Security gate

Drivers call `InsightSecurityGate.AuthorizeAsync` after resolving the row type and before opening the
source. The gate closes `IAccessProvider<rowType, QueryInsightRequest>` and
`IEntitlementProvider<rowType, QueryInsightRequest>` reflectively:

- an access provider can deny the entire source for the request and principal;
- an entitlement provider can return an `Expression<Func<rowType, bool>>` row predicate;
- a missing provider leaves that layer ungated.

`RepositoryDriver` applies the entitlement expression before filter pushdown so row security remains in
the backend query when the provider returns an expression.

## Feature priority table

| Feature | Priority |
| --- | --- |
| `SchemataInsightFeature` | 495,000,000 |
| `SchemataInsightHttpFeature` | 495,100,000 |
| `SchemataInsightGrpcFeature` | 495,200,000 |

## Extension points

- Implement `ISourceDriver` and register it with `AddSourceDriver<TDriver>(name)` for a new backend.
- Implement `IInsightSourceCatalog` to resolve source names from configuration, a database, or another
  registry.
- Register `IInsightRequestAdvisor`, `IInsightPlanAdvisor`, `IInsightSourceAdvisor`, or
  `IInsightResponseAdvisor` for validation, rewrites, source gating, and response shaping.
- Register Security `IAccessProvider<TEntity, QueryInsightRequest>` or
  `IEntitlementProvider<TEntity, QueryInsightRequest>` to restrict source access.

## Caveats

- A query must bind at least one source. Multiple sources must be connected by joins.
- Top-level `Top` and `Skip` transformations are rejected; use `PageSize` and `Skip` on the request.
- Joins run locally with a bounded buffered side because a single backend query cannot span
  heterogeneous sources.
- `RepositoryDriver` needs a repository for each registered resource entity.

## See also

- [Planning](planning.md) — logical plan construction, expression slot resolution, and validation
- [Drivers](drivers.md) — pushdown, residual stages, `RepositoryDriver`, and custom drivers
- [Transports](transports.md) — HTTP and gRPC surfaces
- [Insight Guide](../../guides/insight.md) — first federated query walkthrough
