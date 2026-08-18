# Schemata.Insight.Foundation

## OVERVIEW

Federated **read**-query subsystem. A request binds one or more named sources, plans a node tree, pushes down whatever the source driver can handle, and evaluates the residual locally. This file covers the whole Insight domain — Skeleton (17 files), Foundation (25, ~2551 LOC), Http, Grpc — since no other Insight package has its own file.

## ENTRY POINT

`UseInsight(this SchemataBuilder, Action<SchemataInsightBuilder>?)` in [Extensions/SchemataBuilderExtensions.cs](Extensions/SchemataBuilderExtensions.cs) adds the non-generic `SchemataInsightFeature`. Transports: `MapHttp()` → `SchemataInsightHttpFeature`, `MapGrpc()` → `SchemataInsightGrpcFeature` (protobuf-net.Grpc).

## PERSISTED STATE

One entity: `SchemataInsightSource` ([../Schemata.Insight.Skeleton/Entities/SchemataInsightSource.cs](../Schemata.Insight.Skeleton/Entities/SchemataInsightSource.cs)) — `Driver` plus JSON `Params` per source. It is only used by the **opt-in** database catalog (`SchemataInsightBuilder.UseDatabaseCatalog()`); the in-memory catalog is the default, so a host can run Insight with no Insight tables at all.

## KEY TYPES

- `IInsightService` — the whole public surface: `QueryAsync(request, principal, ct)`.
- `IInsightSourceCatalog` + `SourceConfig` — name → driver + params. Deliberately hidden from the wire; a client names a source, never a driver.
- `ISourceDriver` + `DriverCapabilities` — the pushdown contract. Flags: `Filter`, `Compute`, `Project`, `Order`, `Group`, `Limit`, `Join`, `Nested`.
- `PlanNode` hierarchy — `SourceNode`, `FilterNode`, `ComputeNode`, `GroupNode`, `OrderNode`, `LimitNode`, `SelectionNode`, plus `JoinNode` and `SubPlan` (which carries `EnforceSecurity`).
- [Planning/InsightPlanBuilder.cs](Planning/InsightPlanBuilder.cs) — request → plan. `PlanExecutor` splits single-source subplans for driver pushdown. [Execution/LocalPipelineExecutor.cs](Execution/LocalPipelineExecutor.cs) runs residual stages over alias-nested dictionary rows.
- [Drivers/RepositoryDriver.cs](Drivers/RepositoryDriver.cs) — the built-in driver over `Schemata.Entity.Repository`.
- [Security/InsightSecurityGate.cs](Security/InsightSecurityGate.cs) — the authorization seam.

## EXPRESSIONS COUPLING

Insight resolves `IExpressionCompiler` and `IExpressionPushdownPlanner` **keyed** by the resolved language (`"aip"` / `"cel"`), and `IOrderCompiler` **non-keyed**. The dependency is on `Schemata.Expressions.Skeleton` only — the language packages are the consumer's choice.

## SECURITY

`InsightSecurityGate.AuthorizeAsync<TEntity>` resolves `IAccessProvider<TEntity, QueryInsightRequest>` and `IEntitlementProvider<TEntity, QueryInsightRequest>` from `Schemata.Security.Skeleton`. The row type is a generic parameter of the calling driver, so nothing is closed reflectively. The source-access and row-entitlement gate runs **before** filter pushdown, so an entitlement expression stays inside the backend query rather than becoming a local filter.

## ADVISORS

Four, in [../Schemata.Insight.Skeleton/Advisors/IInsightAdvisors.cs](../Schemata.Insight.Skeleton/Advisors/IInsightAdvisors.cs): `IInsightRequestAdvisor` (pre-planning, may throw), `IInsightPlanAdvisor` (post-planning rewrite), `IInsightSourceAdvisor` (per source, may block), `IInsightResponseAdvisor` (post-execution).

## DEPS

Skeleton → Advice, Expressions.Skeleton. Foundation → Core, Common, Entity.Repository, Expressions.Skeleton, Insight.Skeleton, Security.Skeleton. Http → Insight.Foundation + Transport.Http. Grpc → Insight.Foundation + Transport.Grpc. Consumed by `Schemata.Report.Foundation` through Foundation, never through a transport.

## GOTCHAS

- Top-level `Top` / `Skip` transformations are **rejected** with `UNIMPLEMENTED`. Use `page_size` / `skip` on the request instead.
- A query must bind at least one source, and a multi-source request must be connected by joins — there is no implicit cross product.
- **Joins run locally**, as a nested loop with a bounded buffered side (`MaxResidualScanRows`, default 10 000), because a single backend query cannot span heterogeneous sources. The scan throws rather than materialize an unbounded superset.
- `RepositoryDriver` deliberately does not advertise `Join` or `Nested`. Adding those flags without implementing them silently produces wrong results.
- A computed field needs a value-capable language. CEL registers `SupportsValues = true`; AIP-160 registers `false` and is predicate-only — computes silently have nowhere to run under AIP.
- Registering a source binding is not enough: the driver itself must also be registered as a keyed service. `AddRepositorySource` alone leaves the driver unresolvable.

Canonical docs: `docs/documents/insight/overview.md`, `planning.md`, `drivers.md`, `transports.md`; `docs/cookbook/federated-query.md`.
