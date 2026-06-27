# Schemata.Abstractions — Root Contracts

97 source files. The bottom of the dependency graph: no package outside `Microsoft.Extensions.*` is referenced. Every other Schemata package depends on this one.

## Layout

```
Schemata.Abstractions/
├── IFeature.cs            # Order/Priority contract; shared by features + modules
├── SchemataConstants.cs   # well-known string constants (DI keys, header names, …)
├── Unit.cs                # void-equivalent value type for generic pipelines
├── Advisors/              # IAdvisor + AdviceContext + AdviseResult
├── Entities/              # trait marker interfaces + Operations / Ordering enums
├── Errors/                # Google AIP error model (ErrorBody, *Detail records)
├── Exceptions/            # SchemataException + per-status subclasses
├── Json/                  # PolymorphicAttribute (consumed by Schemata.Core)
├── Modular/               # IModule, ModuleBase, ModuleAttribute
└── Resource/              # AIP resource attributes, request/result base records
```

## Entity Traits

All in [Entities/](Entities/). A trait is a marker interface; advisors react to it with `entity is ITrait` checks.

| Trait | Adds |
|---|---|
| `IIdentifier` | primary key |
| `ITimestamp` | `CreateTime` / `UpdateTime` (advisor-stamped) |
| `ISoftDelete` | `DeleteTime` / `PurgeTime` (advisor-stamped, filtered from queries) |
| `IConcurrency` | optimistic concurrency token |
| `IOwnable` | per-row owner (paired with `Schemata.Entity.Owner`) |
| `IExpiration` | `ExpireTime` filtering |
| `IStateful` | `State` enum + transition support |
| `ITransition` | history of state transitions |
| `IDescriptive` | `Title` / `Description` text |
| `ISourceReference` | external system back-link |
| `ICanonicalName` | AIP resource name parsing (paired with `[CanonicalName]`) |

Two enums power query semantics: [Operations.cs](Entities/Operations.cs) (`Create`/`Get`/`List`/`Update`/`Delete`/...), [Ordering.cs](Entities/Ordering.cs) (`Ascending`/`Descending`).

## Resource Contracts (Google AIP)

All in [Resource/](Resource/). Attributes are how a controller / handler is wired to a resource type.

- Attributes: `[Resource]`, `[Resource<TEntity>]`, `[Resource<TEntity,TKey>]`, `[Resource<TEntity,TKey,TParent>]`, `[Resource<TEntity,TKey,TParent,TGrandparent>]`, `[HttpResource]`, `[GrpcResource]`, `[ResourcePackage]`, `[ResourceMethod]`, `[Anonymous]`, `[ReadAcross]`, `[RateLimitPolicy]`.
- Method scopes: [ResourceMethodScope.cs](Resource/ResourceMethodScope.cs) + [ResourceHttpMethod.cs](Resource/ResourceHttpMethod.cs).
- Request bases: `GetRequest`, `ListRequest`, `DeleteRequest`, `PurgeRequest`, `EmptyResourceRequest`.
- Result bases: `CreateResultBase`, `GetResultBase`, `ListResultBase`, `UpdateResultBase`, `DeleteResultBase`, `EmptyResourceResponse`, `ExpungeResponse`, `PurgeResponse`.
- Long-running operations: `Operation`, `OperationMetadata`, `OperationResponse`, `OperationStatus`.
- Pluggable behaviour interfaces: `IRequestIdentification`, `IUpdateMask`, `IFreshness`, `IValidation`, `IAllowMissing`, `IEntitiesResult`, `IResourceMethodHandler`, `IResourceTypeResolver`.
- `TotalSizeMode` controls AIP list pagination total semantics.

## Error / Exception Model

Mirror of [google.rpc.Status](https://cloud.google.com/apis/design/errors). All in [Errors/](Errors/) + [Exceptions/](Exceptions/).

- `ErrorResponse` wraps an `ErrorBody` plus `IErrorDetail[]`. AIP-flavoured `OAuthErrorResponse` for OAuth 2.0 flows.
- `IErrorDetail` implementations: `BadRequestDetail`, `DebugInfoDetail`, `ErrorInfoDetail`, `HelpDetail`, `LocalizedMessageDetail`, `PreconditionFailureDetail`, `QuotaFailureDetail`, `RequestInfoDetail`, `ResourceInfoDetail`, `RetryInfoDetail`, plus the violation records (`ErrorFieldViolation`, `PreconditionViolation`, `QuotaViolation`, `ErrorHelpLink`).
- Exception hierarchy: `SchemataException` ← `InvalidArgumentException`, `AlreadyExistsException`, `NotFoundException`, `FailedPreconditionException`, `AbortedException`, `QuotaExceededException`, `UnauthenticatedException`, `PermissionDeniedException`, `NoContentException`, `OAuthException`, `TenantResolveException`, `ValidationException`. Each maps to a specific HTTP/gRPC status.

## Modular

[Modular/IModule.cs](Modular/IModule.cs) — empty marker that extends `IFeature` so modules participate in ordering. [Modular/ModuleBase.cs](Modular/ModuleBase.cs) — `Order=0, Priority=Order` default. [Modular/ModuleAttribute.cs](Modular/ModuleAttribute.cs) — discovery hint emitted by `Schemata.Application.Modular.Targets`.

## Conventions

- **One concept per file.** No multi-interface files (look at the existing tree).
- **No async or DI here** — `Schemata.Abstractions` must remain framework-agnostic enough to be referenced from `netstandard2.0` consumers if needed.
- **All XML doc comments are required** on public types — `GenerateDocumentationFile=true` is on for `src/*`.
- **A new trait is just a marker interface** in `Entities/` plus a paired advisor in `Schemata.Entity.Repository` (or wherever the behaviour lives).

## Anti-Patterns

- **Do NOT** add methods to a trait interface — they are marker-only. Behaviour belongs in advisors.
- **Do NOT** add references to ASP.NET Core, EF Core, LinqToDB, or any vendor package from this project; it would push the dependency down to every consumer.
- **Do NOT** introduce a new exception type without picking a deterministic HTTP/gRPC status mapping. Update `Schemata.Transport.Http`'s exception handler at the same time.
- **Do NOT** rename a trait — generated code in `Schemata.Modeling.Generator` references trait names from `.skm` files.

## Notes

- `Unit.cs` exists so generic pipelines can be expressed without `void` special-casing.
- `SchemataConstants.cs` is the single source of truth for HTTP header names (`X-Request-Id`, `Authorization`, …) and DI keys; do not duplicate the strings inline.
- `ResourceAttribute` has five arity-suffixed files (\`1, \`2, \`3, \`4) to support nested parent/grandparent typing — keep them in sync.
