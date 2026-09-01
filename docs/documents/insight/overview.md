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
builder.UseSchemata(schema => {
    schema.UseSecurity();
    var insight = schema.UseInsight(i => {
        i.AddRepositorySource("students", "students")
         .AddSourceDriver<RepositoryDriver>(RepositoryDriver.DriverName);
    });

    insight.WithAuthentication("Bearer")
           .MapHttp();
});
```

`SchemataInsightBuilder` implements `IResourceBuilder`. `WithAuthentication` stores the selected transport scheme through its `ResourceSecurityRegistration`. `WithAuthorization` throws for Insight because source access is configured through `InsightSecurityGate`, which evaluates providers for each source row type.

`MapHttp()` and `MapGrpc()` are concrete Insight transport extensions. Each activates its domain transport feature; shared transport behavior comes from that feature's dependencies.

## Dispatch and advisors

`QueryInsightRequest` is a query. The dispatcher establishes `AdviceContext`, composes registered `IRequestPipelineAdvisor<QueryInsightRequest,QueryInsightResponse>` wraps, and invokes `DefaultQueryInsightHandler`.

The handler builds the plan, places the `PlanNode` in the ambient context for plan-stage coordination, runs `IInsightPlanAdvisor`, and executes the plan. `PlanExecutor` runs `IInsightSourceAdvisor` for each source. Direct calls to the executor create a context only when no ambient context exists.

Register `IRequestPipelineAdvisor<QueryInsightRequest,QueryInsightResponse>` for request-wide rejection or response shaping. Register `IInsightPlanAdvisor` or `IInsightSourceAdvisor` for work that requires the plan or source binding.

## Source security

Drivers call `InsightSecurityGate.AuthorizeAsync<TEntity>` before opening a source. The gate resolves `IAccessProvider<TEntity,QueryInsightRequest>` and `IEntitlementProvider<TEntity,QueryInsightRequest>` from the source scope. An access provider can reject the source; an entitlement provider can return an expression that the Repository driver applies to its backend query.

## See also

- [Planning](planning.md)
- [Drivers](drivers.md)
- [Transports](transports.md)
- [Security](../security.md)
- [Messaging](../messaging/overview.md)
