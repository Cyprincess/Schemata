# src — Runtime Packages

60 packages organised by **`Schemata.{Domain}.{Role}`**. All target `net8.0;net10.0`, ship `.nupkg` + `.snupkg`, and embed XML doc comments.

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

### Expressions (parsers + planners)

- `Skeleton` ← `Order`, `Cel`, `Aip` (siblings independent of each other)

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

### Resource (Google AIP CRUD)

- `Foundation` is the hub (no separate `Skeleton`).
- `Foundation` ← `Http`, `Grpc`

### Scheduling (cron / periodic / one-time jobs)

- `Skeleton` ← `Foundation` ← `Http`, `Grpc`, `Event`
- `Foundation` pulls `Mapping.Skeleton`, `Resource.Foundation`
- `Http` pulls `Resource.Http`; `Grpc` pulls `Resource.Grpc`
- `Event` pulls `Event.Foundation`
- Intentionally in no meta-package (like `Flow.Bpmn`): consumers add an explicit `PackageReference`.

### Security (RBAC/ABAC policies)

- `Skeleton` ← `Foundation`

### Tenancy (multi-tenant resolution + per-tenant DI)

- `Skeleton` (depends on `Entity.Repository`) ← `Foundation`

### Transport (shared HTTP / gRPC plumbing)

- `Transport.Http`, `Transport.Grpc` — no skeleton; both stand alone and are pulled in by the corresponding `*.Http` / `*.Grpc` packages in other domains.

### Validation (FluentValidation integration)

- `Validation.Skeleton` ← `Validation.FluentValidation`

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

- File counts: hot spots are `Schemata.Authorization.Foundation` (120), `Schemata.Flow.Skeleton` (107), `Schemata.Abstractions` (101), `Schemata.Resource.Foundation` (90), `Schemata.Authorization.Skeleton` (72). Each has its own `AGENTS.md` where present.
- The advice generator is auto-attached as an analyzer to every `src/*` project via [../Directory.Build.props](../Directory.Build.props#L90-L94). Skip with `-p:SchemataSkipGenerators=true`.
- All packages share `Schemata.png` / `LICENSE` / root `README.md` for the NuGet display via `PackageIconFullPath` + `PackageReadmeFile` resolved in [../Directory.Build.props](../Directory.Build.props#L120-L130).
