# Documents

The Documents section provides technical reference for every subsystem in the Schemata framework. Each section describes the architecture, interfaces, configuration, and behavior of a specific part of the framework. Start here when you need to understand how something works, not just how to use it.

## Sections

### Core

The foundation that all other features build on.

| Document | Description |
| --- | --- |
| [Overview](core/overview.md) | Package structure and startup sequence |
| [Feature System](core/feature-system.md) | SchemataBuilder, FeatureBase, feature lifecycle, DependsOn |
| [Advice Pipeline](core/advice-pipeline.md) | IAdvisor, AdviceContext, AdviseResult, pipeline execution |
| [Built-in Features](core/built-in-features.md) | Authoritative priority table for all built-in and extension features |
| [JSON Serialization](core/json-serialization.md) | snake_case, long-as-string, polymorphic types |
| [Error Model](core/error-model.md) | Exception hierarchy, structured error responses |

### Advice

The source generator and runtime that power the advisor pipeline.

| Document | Description |
| --- | --- |
| [Overview](advice/overview.md) | AdvicePipeline, AdviceRunner, short-circuit semantics |
| [Runtime](advice/runtime.md) | AdviceRunner family (arity 1..16), generated extension methods |
| [Generator](advice/generator.md) | Schemata.Advice.Generator emission rules and gotchas |

### Entity

The entity modeling layer.

| Document | Description |
| --- | --- |
| [Overview](entity/overview.md) | Trait system overview |
| [Traits](entity/traits.md) | All trait interfaces and their advisor mappings |

### Repository

The data access layer.

| Document | Description |
| --- | --- |
| [Overview](repository/overview.md) | IRepository surfaces, Once(), Suppress*() methods |
| [Mutation Pipeline](repository/mutation-pipeline.md) | Add/Update/Remove advisor chains |
| [Query Pipeline](repository/query-pipeline.md) | BuildQuery/Query/Result advisor chains |
| [Unit of Work](repository/unit-of-work.md) | BeginWork semantics, EnqueueAfterCommit |
| [Caching](repository/caching.md) | Query result caching, cross-link to caching section |
| [Ownership](repository/ownership.md) | Schemata.Entity.Owner, IOwnable |
| [Providers](repository/providers.md) | EF Core and LinqToDB setup, Detach before Update, search-path gaps |

### Caching

The distributed cache layer.

| Document | Description |
| --- | --- |
| [Overview](caching/overview.md) | ICacheProvider, key format, layer stack |
| [Distributed](caching/distributed.md) | DistributedCacheProvider, index locks |
| [Redis](caching/redis.md) | RedisCacheProvider, meta key TTL refresh |
| [Query Cache](caching/query-cache.md) | Four advisors, reverse index, after-commit eviction |

### Resource

The CRUD service layer and transport bindings.

| Document | Description |
| --- | --- |
| [Overview](resource/overview.md) | Four type parameters, handler stages, type collapsing |
| [Create Pipeline](resource/create-pipeline.md) | Create operation, idempotency |
| [Read Pipeline](resource/read-pipeline.md) | List and Get operations |
| [Update Pipeline](resource/update-pipeline.md) | Field masks, concurrency, freshness |
| [Delete Pipeline](resource/delete-pipeline.md) | Soft-delete, physical delete |
| [Resource Naming](resource/resource-naming.md) | AIP-122 canonical names, ResourceNameDescriptor |
| [HTTP Transport](resource/http-transport.md) | MapHttp, controller synthesis, route conventions |
| [gRPC Transport](resource/grpc-transport.md) | MapGrpc, code-first service synthesis |
| [Filtering](resource/filtering.md) | AIP-160 filter grammar, AIP-132 ordering, pagination |

### Expressions

The filter and order-by compiler stack.

| Document | Description |
| --- | --- |
| [Overview](expressions/overview.md) | IExpressionCompiler, IOrderCompiler, ExpressionCache LRU |
| [AIP](expressions/aip.md) | Hand-written Parlot parser, operators, functions |
| [CEL](expressions/cel.md) | CEL macros, conformance tests, missing IOrderCompiler |
| [Custom Language](expressions/custom-language.md) | Authoring a keyed IExpressionCompiler |

### Event

The event bus and dispatch pipeline.

| Document | Description |
| --- | --- |
| [Overview](event/overview.md) | Contracts, wire names vs CLR names, IEventTypeRegistry |
| [Dispatch Pipeline](event/dispatch-pipeline.md) | Publish/consume advisor pairs, routing |
| [Providers](event/providers.md) | InProcess and RabbitMQ transports |

`EventType` carries the wire name registered via `IEventTypeRegistry` end-to-end: `EventContext.EventType` and the `SchemataEvent.EventType` audit column hold the same string. See [Event Overview](event/overview.md).

### Scheduling

The job scheduler and lifecycle observer pipeline.

| Document | Description |
| --- | --- |
| [Overview](scheduling/overview.md) | IScheduler, IScheduledJob, schedule kinds |
| [Triggers](scheduling/triggers.md) | Cronos cron, periodic, one-shot, missed-fire policy |
| [Jobs](scheduling/jobs.md) | IJobExecutionAdvisor pipeline, IJobLifecycleObserver (Proceed/Skip/Block) |
| [Persistence](scheduling/persistence.md) | SchemataJob and SchemataJobExecution rows |
| [Event Integration](scheduling/event-integration.md) | UseSchedulingEvent, EventPublishingJobLifecycleObserver, InterceptExecution behavior change |

`SchemataSchedulingEventOptions.InterceptExecution=true` maps to `JobTriggerOutcome.Skip`: the scheduled job is not executed, the execution row is marked `Cancelled`, and the schedule advances to the next occurrence. To freeze the schedule on a triggered fire, return `JobTriggerOutcome.Block` from a custom `IJobLifecycleObserver`. Migration from prior advisor-based configurations is covered in [Event Integration](scheduling/event-integration.md).

### Flow

The BPMN 2.0.2 process engine.

| Document | Description |
| --- | --- |
| [Overview](flow/overview.md) | Architecture and package structure |
| [AST Reference](flow/ast.md) | FlowElement type hierarchy |
| [DSL Reference](flow/dsl.md) | Fluent builder API for process graphs |
| [Engine](flow/engine.md) | Single-token state machine engine |
| [Validator](flow/validator.md) | Process definition validation rules |
| [Runtime Services](flow/runtime.md) | ProcessRuntime, endpoints, registration |
| [State Machine](flow/state-machine.md) | Default engine wiring via SchemataFlowFeature |
| [Event Integration](flow/event.md) | UseFlowEvent, SchemataFlowEventFeature, FlowEventTransitionObserver |
| [Scheduling Integration](flow/scheduling.md) | UseFlowScheduling, SchemataFlowSchedulingFeature, FlowTimerJob |
| [HTTP Transport](flow/http.md) | UseFlowHttp, SchemataFlowHttpFeature, ProcessController |
| [gRPC Transport](flow/grpc.md) | UseFlowGrpc, SchemataFlowGrpcFeature, ProcessService |

`Schemata.Flow.StateMachine` ships the default `IFlowRuntime`; `SchemataFlowFeature` wires it directly so `UseFlow()` makes it available without further configuration. HTTP and gRPC surfaces are added with `UseFlowHttp()` and `UseFlowGrpc()` respectively.

### Mapping

The object-to-object mapping abstraction.

| Document | Description |
| --- | --- |
| [Overview](mapping/overview.md) | ISimpleMapper, SimpleMapperHelper, UpdateMask |
| [AutoMapper](mapping/automapper.md) | Adapter behavior, null skip |
| [Mapster](mapping/mapster.md) | Adapter behavior, IgnoreNullValues |

### Cross-cutting

| Document | Description |
| --- | --- |
| [Validation](validation.md) | FluentValidation integration, IValidationAdvisor |
| [Security](security.md) | Access providers, entitlement-based query filtering |
| [Identity](identity.md) | ASP.NET Core Identity, generic parameters |
| [Authorization](authorization.md) | OAuth 2.0 / OIDC server |
| [Tenancy](tenancy.md) | Multi-tenant resolution, per-tenant DI |
| [Modules](modules.md) | Build-time discovery, runtime load order, and the `IModule` lifecycle that turns a referenced assembly into a host plug-in |
| [Packages](packages.md) | Meta-package matrix |

## See also

- [Guides](../guides/overview.md) — step-by-step tutorials
- [Cookbook](../cookbook/overview.md) — scenario-driven recipes
- [Modeling](../modeling/overview.md) — SKM language reference
