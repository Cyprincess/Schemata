# Documents

Technical reference for every subsystem in Schemata. Each section covers a subsystem's
architecture, key types, behavior, configuration, and extension points. Read here to understand
how a part of the framework works and why it is built that way.

## Core

The foundation that the other features build on.

| Document | Covers |
| --- | --- |
| [Overview](core/overview.md) | Package layout and the startup sequence |
| [Feature System](core/feature-system.md) | `SchemataBuilder`, `FeatureBase`, the three-phase lifecycle, `DependsOn` |
| [Advice Pipeline](core/advice-pipeline.md) | `IAdvisor`, `AdviceContext`, `AdviseResult`, pipeline execution |
| [Built-in Features](core/built-in-features.md) | The authoritative priority table for built-in and extension features |
| [JSON Serialization](core/json-serialization.md) | snake_case, long-as-string, polymorphic discriminator |
| [Error Model](core/error-model.md) | Exception hierarchy and the structured error response |

## Advice

The source generator and runtime behind the advisor pipeline.

| Document | Covers |
| --- | --- |
| [Overview](advice/overview.md) | `AdvicePipeline`, the runner, short-circuit semantics |
| [Runtime](advice/runtime.md) | The `AdviceRunner` family (arity 1..16) and generated extension methods |
| [Generator](advice/generator.md) | `Schemata.Advice.Generator` emission rules |

## Entity

The trait-based entity model.

| Document | Covers |
| --- | --- |
| [Overview](entity/overview.md) | The trait system and key resolution |
| [Traits](entity/traits.md) | Every trait interface and the advisor that acts on it |

## Repository

The data-access layer.

| Document | Covers |
| --- | --- |
| [Overview](repository/overview.md) | `IRepository` surface, query and mutation methods, suppression scopes |
| [Mutation Pipeline](repository/mutation-pipeline.md) | Add, update, and remove advisor chains |
| [Query Pipeline](repository/query-pipeline.md) | Build-query, query, and result advisor chains |
| [Unit of Work](repository/unit-of-work.md) | Explicit enlistment and commit |
| [Caching](repository/caching.md) | Query-result caching from the repository's side |
| [Ownership](repository/ownership.md) | `Schemata.Entity.Owner` and `IOwnable` |
| [Providers](repository/providers.md) | EF Core and LinqToDB setup and caveats |

## Caching

The distributed cache layer.

| Document | Covers |
| --- | --- |
| [Overview](caching/overview.md) | `ICacheProvider`, the key format, provider stack |
| [Distributed](caching/distributed.md) | `DistributedCacheProvider` over `IDistributedCache` |
| [Redis](caching/redis.md) | `RedisCacheProvider` and its compare-and-set scripts |
| [Query Cache](entity/query-cache.md) | Query and result advisors, the reverse index, committed eviction |

## Resource

The AIP-aligned CRUD service and its transports.

| Document | Covers |
| --- | --- |
| [Overview](resource/overview.md) | The four type parameters, handler stages, type collapsing |
| [Create Pipeline](resource/create-pipeline.md) | Create, request sanitization, idempotency |
| [Read Pipeline](resource/read-pipeline.md) | List and Get |
| [Update Pipeline](resource/update-pipeline.md) | Field masks, freshness, soft-deleted handling |
| [Delete Pipeline](resource/delete-pipeline.md) | Soft and hard delete |
| [Resource Naming](resource/resource-naming.md) | AIP-122 canonical names and the name descriptor |
| [Custom Methods](resource/custom-methods.md) | AIP-136 verb-noun methods on a resource |
| [HTTP Transport](resource/http-transport.md) | REST controller synthesis and route conventions |
| [gRPC Transport](resource/grpc-transport.md) | Code-first gRPC service synthesis |
| [Filtering](resource/filtering.md) | AIP-160 filter, AIP-132 order, pagination |

## Expressions

The filter and order-by compiler stack.

| Document | Covers |
| --- | --- |
| [Overview](expressions/overview.md) | `IExpressionCompiler`, `IOrderCompiler`, the expression cache |
| [AIP](expressions/aip.md) | The AIP-160 parser, operators, and functions |
| [CEL](expressions/cel.md) | The CEL compiler and its conformance scope |
| [Custom Language](expressions/custom-language.md) | Authoring a keyed `IExpressionCompiler` |

## Event

The event bus and dispatch pipeline.

| Document | Covers |
| --- | --- |
| [Overview](event/overview.md) | Contracts, wire names, `IEventTypeRegistry` |
| [Dispatch Pipeline](event/dispatch-pipeline.md) | Publish to the outbox, drain, consume |
| [Providers](event/providers.md) | In-process and RabbitMQ transports |

## Scheduling

The job scheduler and its observers.

| Document | Covers |
| --- | --- |
| [Overview](scheduling/overview.md) | `IScheduler`, `IScheduledJob`, schedule kinds |
| [Triggers](scheduling/triggers.md) | Cron, periodic, one-time, missed-fire policy |
| [Jobs](scheduling/jobs.md) | The execution advisor and the lifecycle observer |
| [Persistence](scheduling/persistence.md) | The `SchemataJob` and `SchemataJobExecution` rows |
| [Event Integration](scheduling/event-integration.md) | Publishing lifecycle events to the bus |

## Push

The broadcast notification fan-out layer.

| Document | Covers |
| --- | --- |
| [Overview](push/overview.md) | `IPushService`, `IPushTransport`, targets, startup |
| [Dispatch](push/dispatch.md) | Fan-out, self-filtering, streaming order, isolation, the advisor |
| [Subscriptions](push/subscriptions.md) | `SchemataPushSubscription`, the manager, ownership |
| [Scheduling](push/scheduling.md) | Durable scheduled dispatch over `IOperationDispatcher` |

## Flow

The BPMN process engine.

| Document | Covers |
| --- | --- |
| [Overview](flow/overview.md) | Architecture and package layout |
| [AST](flow/ast.md) | The `FlowElement` hierarchy |
| [DSL](flow/dsl.md) | The typed builder for process graphs |
| [Engine](flow/engine.md) | The single-token state-machine engine |
| [Validator](flow/validator.md) | Process-definition validation rules |
| [Runtime Services](flow/runtime.md) | `ProcessRuntime`, persistence, registration |
| [State Machine](flow/state-machine.md) | The default engine wiring |
| [Event Integration](flow/event.md) | Message and signal catches on the event bus |
| [Scheduling Integration](flow/scheduling.md) | Timer catches on the scheduler |
| [HTTP Transport](flow/http.md) | The process surface over HTTP |
| [gRPC Transport](flow/grpc.md) | The process surface over gRPC |

## Mapping

The object-to-object mapping abstraction.

| Document | Covers |
| --- | --- |
| [Overview](mapping/overview.md) | `ISimpleMapper`, merge vs mask, the mask tree |
| [AutoMapper](mapping/automapper.md) | The AutoMapper adapter |
| [Mapster](mapping/mapster.md) | The Mapster adapter |

## Cross-cutting

| Document | Covers |
| --- | --- |
| [Validation](validation.md) | FluentValidation integration and the validation advisors |
| [Security](security.md) | Access providers and entitlement-based query filtering |
| [Identity](identity.md) | ASP.NET Core Identity integration |
| [Authorization](authorization.md) | The OAuth 2.0 / OpenID Connect server |
| [Tenancy](tenancy.md) | Tenant resolution and per-tenant DI |
| [Modules](modules.md) | Discovery, load order, and the `IModule` lifecycle |
| [Packages](packages.md) | The meta-package matrix |
