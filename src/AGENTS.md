# src — Runtime Packages

66 packages organised by **`Schemata.{Domain}.{Role}`**. All target `net8.0;net10.0`, ship `.nupkg` + `.snupkg`, and embed XML doc comments.

## Suffix Vocabulary

| Suffix | Role |
|---|---|
| `Skeleton` | contracts, models, pipeline core, no ASP.NET dependency where possible |
| `Foundation` | runtime / use-case orchestration; the `UseXxx` feature lives here |
| `Http` / `Grpc` | transport adapter on top of `Foundation` |
| `Event` | bridge from a domain to the event bus |
| `Scheduling` | bridge from a domain to the scheduler |
| `StateMachine` | alternate runtime for the BPMN AST |
| `EntityFrameworkCore` / `LinqToDB` | persistence adapter |
| `Redis` / `Distributed` / `Cache` | cache adapter / wrapper |
| `RabbitMq` | message broker adapter |
| `AutoMapper` / `Mapster` | mapping vendor adapter |
| `FluentValidation` | validation vendor adapter |
| `Identity` (as suffix) | Identity-bridging variant (Authorization.Identity ties OAuth to ASP.NET Core Identity) |
| `Owner` | per-entity ownership advisor for the repository pipeline |
| `Bpmn` | full BPMN 2.0.2 alternate engine for the Flow AST |
| `Aip` / `Cel` / `Order` | expression-language implementations behind `Expressions.Skeleton` |
| `Transport` (as domain) | shared HTTP / gRPC / RabbitMQ plumbing; no Skeleton, pulled in by every other domain's `.Http` / `.Grpc` / `.RabbitMq` |
| `Modular` (bare) | module discovery and loading on top of `Core` |

## Domain Map

Skeleton/Foundation arrows are intra-domain; siblings without arrows do not depend on each other.

### Platform core (no domain prefix)

- `Schemata.Abstractions` — root contracts (`IFeature`, `IModule`, entity traits, AIP resource attrs, exception types). Hub for everything below.
- `Schemata.Common` — shared value types, JSON options, hashing, predicate helpers, canonical-name primitives (`ResourceRequestContainer`, `ResourceIdentifiers`, `ResourceNameDescriptor`, `IPagination`, `PaginationExtensions`). Depends on `Abstractions`.
- `Schemata.Advice` — advisor runtime + generated `RunAsync` glue. Depends on `Abstractions`.
- `Schemata.Core` — `SchemataBuilder`, `SchemataStartup`, every built-in ASP.NET feature. Depends on `Common`.
- `Schemata.Modular` — module discovery + runner. Depends on `Core`.

### Authorization (OAuth 2.0 / OIDC server)

- `Skeleton` ← `Foundation`
- `Foundation` ← `Identity` (ties the OAuth server to ASP.NET Core Identity)
- `Skeleton` surface: entities `SchemataApplication` / `SchemataAuthorization` / `SchemataScope` / `SchemataToken` / `SchemataSubjectMapping`; managers `IApplicationManager` / `IAuthorizationManager` / `IScopeManager` / `ITokenManager`; contracts `IClientAuthentication`, `ISubjectProvider`, `ISubjectIdentifierService`, `IPairwiseSubjectTranslator`, `ILogoutNotifier`; value types `ScopeParser`, `AuthorizationResult`/`Status`, `ConsentDecision`. Depends only on `Entity.Repository`.

### Caching (cache abstraction + adapters)

- `Skeleton` ← `Distributed` (wraps `IDistributedCache`)
- `Skeleton` ← `Redis` (StackExchange.Redis backend)

### Entity (repository, UoW, ownership, query cache, ORM adapters)

- `Repository` is the hub. Key/index discovery uses `[SchemataPrimaryKey]` / `[SchemataIndex]` from `Abstractions`; the contract layer references no ORM (EF Core abstractions included).
- `Repository` ← `EntityFrameworkCore`, `LinqToDB`, `Owner`
- `Repository` + `Caching.Skeleton` ← `Cache`

### Event (in-process + RabbitMQ bus)

- `Skeleton` ← `Foundation` ← `RabbitMq`
- `RabbitMq` also pulls `Transport.RabbitMq` for the shared broker connection and the correlation tracker; it holds no `ConnectionFactory` of its own

### Expressions (parsers + planners)

- `Skeleton` ← `Order`, `Cel`, `Aip` (siblings independent of each other)
- No `src/` csproj references `Aip`, `Cel` or `Order`. Insight, Resource and Flow depend on `Expressions.Skeleton` only; consumers pick the language package through a meta target or an explicit `PackageReference`.
- Compilers, pushdown planners and language descriptors are keyed DI services (`"aip"` / `"cel"`); `IOrderCompiler` is the one non-keyed registration.

### Flow (BPMN process engine)

- `Skeleton` ← `Foundation` ← `Http`, `Grpc`, `Event`, `Scheduling`, `StateMachine`
- `Foundation` also pulls `Core`, `Event.Skeleton`, `Entity.Owner`, `Entity.Repository`, `Expressions.Skeleton` — canonical-name primitives come from `Schemata.Common`, never from another domain's Foundation
- `Http` pulls `Resource.Http`; `Grpc` pulls `Resource.Grpc`
- `Event` pulls `Event.Foundation`; `Scheduling` pulls `Scheduling.Foundation` (direct)
- `StateMachine` = default runtime engine (subset of BPMN 2.0.2)

### Identity (ASP.NET Core Identity integration)

- `Skeleton` (depends on `Entity.Repository`) ← `Foundation`

### Insight (federated query / analytics)

- `Skeleton` ← `Foundation` ← `Http`, `Grpc`
- `Foundation` pulls `Entity.Repository`, `Expressions.Skeleton`, `Security.Skeleton`

### Mapping (object mapping abstraction)

- `Skeleton` ← `Foundation` ← `AutoMapper`, `Mapster`

### Push (notification scheduling)

- `Skeleton` ← `Foundation` (uses `Entity.Owner`) ← `Scheduling` (uses `Scheduling.Foundation`)

### Report (Insight-backed snapshots)

- `Skeleton` ← `Foundation` ← `Http`, `Grpc`, `Scheduling`
- `Skeleton` depends on `Insight.Skeleton`; `Foundation` pulls `Insight.Foundation` and `Scheduling.Skeleton`
- Three entities: `SchemataReport` → `SchemataReportSnapshot` → `SchemataReportSnapshotChunk`
- Nothing in `src/` consumes Report — it sits at the top of the dependency graph.

### Resource (Google AIP CRUD)

- `Foundation` is the hub (no separate `Skeleton`).
- `Foundation` ← `Http`, `Grpc`

### Scheduling (cron / periodic / one-time jobs)

- `Skeleton` ← `Foundation` ← `Http`, `Grpc`, `Event`
- `Foundation` pulls `Core`, `Entity.Repository`, `Mapping.Skeleton`
- `Http` pulls `Resource.Http`; `Grpc` pulls `Resource.Grpc`
- `Event` pulls `Event.Foundation`
- Intentionally in no meta-package (like `Flow.Bpmn`): consumers add an explicit `PackageReference`.

### Security (RBAC/ABAC policies)

- `Skeleton` ← `Foundation`

### Tenancy (multi-tenant resolution + per-tenant DI)

- `Skeleton` (depends on `Entity.Repository`) ← `Foundation`

### Transport (shared HTTP / gRPC / RabbitMQ plumbing)

- `Transport.Http`, `Transport.Grpc` — no skeleton; both stand alone and are pulled in by the corresponding `*.Http` / `*.Grpc` packages in other domains.
- `Transport.RabbitMq` — owns the one `IConnection` every RabbitMQ client in the process shares, plus `RabbitMqConnectionOptions` and `CorrelationTracker`. Entry point is `AddRabbitMqTransport()`; it references no Schemata package at all, carries no feature and takes no priority band.

### Validation (FluentValidation integration)

- `Validation.Skeleton` ← `Validation.FluentValidation`

## Application Assembly Rules (F1–F10)

Every service registration, middleware install and endpoint mapping reaches the host through a feature. These ten rules govern that path.

- **F1 — a feature is the only assembly channel.** `SchemataStartup` is the one `IStartupFilter`; do not add another, and do not build an equivalent path outside the feature pipeline.
- **F2 — no feature, no priority.** The `Priority` / `Order` bands exist to sequence features. A package that carries no feature takes no band.
- **F3 — placement decides whether a component goes through a feature.** Answer one design question: is this component meant to be usable with no Schemata lifecycle at all? *Standalone-usable* components deliberately do not reference `Schemata.Core` and expose an `IServiceCollection` entry point. *Ecosystem* components depend on feature ordering, `DependsOn` and `SchemataOptions`, and must assemble through a feature. Which one a package is, is a ruling — see below — never inferred from its current dependency graph.
- **F4 — what standalone-usable promises.** No reference to `Schemata.Core`; the public entry point is an `IServiceCollection` extension; **no dependency on any ecosystem component**. The third is the one that breaks: depend on a Foundation that carries a feature and the component can no longer run without Schemata, so it is an ecosystem component and needs a feature.
- **F5 — a feature declares, an extension method implements.** Lifecycle methods answer *when* (`Priority`), *after what* (`DependsOn`) and *exactly once*; the capability lives in a public `IServiceCollection` / `IApplicationBuilder` / `IEndpointRouteBuilder` extension that can be called and tested without a feature. An environment guard followed by builder calls is still a declaration; loops, LINQ pipelines, runtime reflection and private helpers on the feature class are not.
- **F6 — `.Skeleton` holds contracts, not DI wiring.** Exemptions are ruled, never claimed; see below.
- **F7 — parts are transparent to each other; probing is banned.** No checking whether a service is registered, no reading options to discover an optional package, no flag field standing for "X is installed". Optional dependencies have exactly two legal forms: resolve at runtime (`GetService` / `GetServices` — absent means not enabled), or declare a hard prerequisite with `DependsOn`. The capability interface belongs to the **consumer's** own Skeleton or Foundation, never to the bridge package; name it for the role it plays — handler / observer / advisor — never `Bridge`.
- **F8 — a root contract is decided by consumption breadth, not by its name.** Measure how many domains actually consume it before moving it. Multi-domain contracts stay in `Schemata.Abstractions`; single-domain contracts belong to that domain's Skeleton.
- **F9 — bridges buy optionality, not "cross-package".** A `Schemata.A.B` bridge exists so that A still works without it. A mandatory dependency between Foundations does not get a bridge; it gets a direct reference **plus** the matching `[DependsOn<TFeature>]`. The architectural assertion is therefore "a direct Foundation-to-Foundation reference must carry its `DependsOn` declaration", which is stricter than banning the reference.
- **F10 — an optional backend provider gets no feature of its own.** A package that swaps the implementation of a contract the domain already declared adds no lifecycle work, so it needs no feature and no band. It registers through the domain's builder or an `IServiceCollection` extension, where staged registration lands before the feature runs and wins under `TryAdd` without `Replace` and without probing. The moment it needs lifecycle work or a `DependsOn`, it stopped being a replacement and becomes an ecosystem component under F3.

### Ruled exemptions

Exemptions are decided by the repository owner. Code does not grant itself one, and a comment or doc line asserting its own exemption is not a ruling.

#### F6 — `.Skeleton` packages that do register services

These three are the complete set; every other `.Skeleton` package declares no `IServiceCollection` extension.

| Package | Surface | Why it is allowed |
|---|---|---|
| `Schemata.Scheduling.Skeleton` | `AddScheduledJob<T>()` | Registers a contract the **consumer** implements, so a consumer depends on the Skeleton alone instead of dragging in `Scheduling.Foundation`. Body uses only `Configure` + `TryAddTransient`. |
| `Schemata.Security.Skeleton` | `AddAccessProvider<T, TRequest, TProvider>()`, `AddEntitlementProvider(Type)`, `AddPermissionResolver<TResolver>()`, `AddPermissionMatcher<TMatcher>()` | Same shape. Body uses only `TryAddScoped`. |
| `Schemata.Mapping.Skeleton` | `Map<TSource, TDestination>()` | Same shape. Body uses only `Configure`. |

The common ground: each one registers an implementation the consumer wrote, using order-independent primitives only. None touches the pipeline, sets a flag, or creates a second activation path.

`Schemata.Identity.Skeleton` is **not** an F6 case — it declares no `IServiceCollection` extension at all. Its documented deviation is that it ships concrete stores and a manager, which is a different question and is not settled here.

#### F3 — packages ruled standalone-usable

| Package(s) | Why |
|---|---|
| `Schemata.Caching.Distributed`, `Schemata.Caching.Redis` | Reference `Caching.Skeleton` only — pure contracts. |
| `Schemata.Expressions.Aip`, `.Cel`, `.Order` | Reference `Common` and `Expressions.Skeleton` only; `Common` is a base component and the Skeleton is pure contracts. |
| `Schemata.Validation.FluentValidation` | References `Abstractions`, `Advice`, `Validation.Skeleton`; no ecosystem dependency, and `AddValidator<>()` uses order-independent primitives only. |
| `Schemata.Entity.Cache`, `.Owner`, `.EntityFrameworkCore`, `.LinqToDB` | Follow `Entity.Repository`'s standing position; each was checked to hold F4's three promises. |
| `Schemata.Transport.RabbitMq` | References no Schemata package at all — only `RabbitMQ.Client` plus the Microsoft DI and Options abstractions. Its entry point is the `AddRabbitMqTransport()` `IServiceCollection` extension. |

`Schemata.Mapping.AutoMapper` and `.Mapster` are **ecosystem** components and already compliant: `UseAutoMapper()` / `UseMapster()` register through `SchemataMappingFeature<T>`, which lives in `Mapping.Foundation` so both adapters share it.

`Schemata.Event.RabbitMq` is an **ecosystem** component holding a ruled exemption from carrying its own feature: it is an optional backend for `Event.Foundation` that registers one consumer and one producer, and a dedicated feature plus priority band would not pay for itself. It reaches the container through the staged registration on `EventProducerBuilder` / `EventConsumerBuilder`, which lands before the feature and so wins over the in-process default.

## Conventions

- **One feature class per domain entry point**: `SchemataXxxFeature` exposed via `UseXxx(this SchemataBuilder)` in `Extensions/SchemataBuilderExtensions.cs`.
- **Builders go in `Builders/` or at the package root**: e.g. `SchemataResourceBuilder.cs`, `Builders/SchedulingBuilder.cs`. They are the fluent surface for a domain.
- **All public types document via XML comments**; `GenerateDocumentationFile=true` is set globally for `src/*`.
- **Cross-cutting behaviour ships as an `IAdvisor<...>`** registered alongside the pipeline, never as a base-class hook.
- **`Skeleton` packages keep ASP.NET out of their dependencies** when possible — `Foundation` is where ASP.NET wiring lives.

## Anti-Patterns

- **Do NOT** add a `Foundation` reference from another domain's `Skeleton` — keeps consumers free to pick implementations.
- **Do NOT** introduce a new domain prefix without a `Foundation` and the matching `Use{Domain}` extension; consumers expect that shape.
- **Do NOT** put feature `Order`/`Priority` magic numbers inline — extend the table in [../README.md](../README.md) and reference the same constant.
- **Do NOT** add ConfigureAwait calls — `ConfigureAwait.Fody` is wired into every `src/*` project ([../Directory.Build.props](../Directory.Build.props#L87)) and rewrites awaits at build time.

## Notes

- File counts: hot spots are `Schemata.Authorization.Foundation` (121), `Schemata.Flow.Skeleton` (109), `Schemata.Abstractions` (104), `Schemata.Resource.Foundation` (85), `Schemata.Authorization.Skeleton` (72), `Schemata.Identity.Skeleton` (49).
- Packages with their own `AGENTS.md`: Abstractions, Advice, Authorization.Foundation, Authorization.Skeleton, Common, Core, Entity.Repository, Expressions.Skeleton, Flow.Bpmn, Flow.Foundation, Flow.Skeleton, Identity.Skeleton, Insight.Foundation, Report.Foundation, Resource.Foundation, Scheduling.Foundation. Several of those cover their whole domain, not just their own package — Expressions.Skeleton covers Aip/Cel/Order, Scheduling.Foundation covers the Skeleton, Report.Foundation and Insight.Foundation cover all layers of their domains.
- The advice generator is auto-attached as an analyzer to every `src/*` project via [../Directory.Build.props](../Directory.Build.props#L90-L94). Skip with `-p:SchemataSkipGenerators=true`.
- All packages share `Schemata.png` / `LICENSE` / root `README.md` for the NuGet display via `PackageIconFullPath` + `PackageReadmeFile` resolved in [../Directory.Build.props](../Directory.Build.props#L120-L130).
