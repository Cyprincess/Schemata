# Insight

Insight executes federated read queries. A `QueryInsightRequest` binds named sources, joins, transformations, selections, and paging data; HTTP and gRPC submit the same request through `IRequestDispatcher`.

## Packages

| Package | Role |
| --- | --- |
| `Schemata.Insight.Skeleton` | Query wire contracts, source catalog contracts, drivers, plans, and entities |
| `Schemata.Insight.Foundation` | Builder, planning, execution, catalog implementations, and `InsightSecurityGate` |
| `Schemata.Insight.Http` / `Schemata.Insight.Grpc` | HTTP controller and gRPC service activation |

## Startup

```csharp
using Schemata.Insight.Foundation;
using Schemata.Insight.Foundation.Drivers;

builder.UseSchemata(schema => {
    schema.UseSecurity();
    var insight = schema.UseInsight(i => {
        i.AddRepositorySource<Student, StudentRow>("students",
                s => new StudentRow { FullName = s.FullName, Age = s.Age })
         .AddSourceDriver<RepositoryDriver>(RepositoryDriver.DriverName);
    });

    insight.WithAuthentication("Bearer")
           .MapHttp();
});

public sealed class StudentRow
{
    public string? FullName { get; set; }
    public int     Age      { get; set; }
}
```

`SchemataInsightBuilder` implements `IResourceBuilder`. `WithAuthentication` stores the selected transport scheme through its `ResourceSecurityRegistration`. `WithAuthorization` throws for Insight because source access is configured through `InsightSecurityGate`, which evaluates providers for each source row type.

`MapHttp()` and `MapGrpc()` are concrete Insight transport extensions. Each activates its domain transport feature; shared transport behavior comes from that feature's dependencies.

## Dispatch and advisors

`QueryInsightRequest` is a query. The dispatcher establishes `AdviceContext`, composes registered `IRequestPipelineAdvisor<QueryInsightRequest,QueryInsightResponse>` wraps, and invokes `DefaultQueryInsightHandler`.

The handler builds an `InsightPlanContext` containing the request and mutable plan, runs ordered `IInsightPlanAdvisor` rewrites, then executes its final `Plan`. `PlanExecutor` runs `IInsightSourceAdvisor` for each source. Direct executor calls create an advice context only when none exists.

Register `IRequestPipelineAdvisor<QueryInsightRequest,QueryInsightResponse>` for request-wide rejection or response shaping. Register `IInsightPlanAdvisor` or `IInsightSourceAdvisor` for work that requires the plan or source binding.

## Source security

Drivers call `InsightSecurityGate.AuthorizeAsync<TEntity>` before opening a source. The gate resolves `IAccessProvider<TEntity,QueryInsightRequest>` and `IEntitlementProvider<TEntity,QueryInsightRequest>` from the source scope. An access provider can reject the source; an entitlement provider can return an expression that the Repository driver applies to its backend query.

## See also

- [Planning](planning.md)
- [Drivers](drivers.md)
- [Transports](transports.md)
- [Security](../security.md)
- [Messaging](../messaging/overview.md)
