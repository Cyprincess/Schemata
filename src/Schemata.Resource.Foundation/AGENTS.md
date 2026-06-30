# Schemata.Resource.Foundation

## OVERVIEW

AIP-compliant resource service. CRUD + custom methods (AIP-136). 90 C# files, ~61 in `Advisors/`. Family: Foundation + Resource.Http + Resource.Grpc as siblings, never cross-referencing. No Skeleton package; entities are user-defined.

## STRUCTURE

- `Features/SchemataResourceFeature.cs`: attribute discovery via `[Resource]` (`ResourceAttribute` in `Schemata.Abstractions/Resource/`); depends on `SchemataRoutingFeature`, `SchemataMappingFeature`, `SchemataSecurityFeature`.
- `Handlers/`: `ResourceOperationHandler<,,,>` split by verb partials (`.Create`/`.Update`/`.Get`/`.List`/`.Delete.cs`); `ResourceMethodOperationHandler<,,>` for AIP-136 custom methods. Runner: `Internal/ResourcePipelineRunner.cs`.
- Soft-delete lifecycle: `UndeleteHandler`, `ExpungeHandler`, `PurgeHandler`/`PurgeFilter`/`PurgeJob` (scheduled via Scheduling), `PurgeJobKeyResolver`.
- `Advisors/`: ~30 built-in `Advice*.cs` plus per-verb interfaces (`IResourceCreateAdvisor<TEntity,TRequest>`, `IResourceListResponseAdvisor<TSummary>`, etc.). Naming `Advice<Verb><Aspect>`; ordering anchors via `DefaultOrder = AdviceCreateRequestAnonymous.DefaultOrder + 10_000_000`. Topics: sanitize, validation, idempotency, freshness, parent/child, read-mask, anonymous-vs-authorize variants per verb.
- `ResourceAdviceContext`: suppression flags from `SchemataResourceOptions` (`CreateRequestValidationSuppressed`, `FreshnessSuppressed`, etc.) injected before the pipeline runs; advisors consult flags and skip.
- `Models/`: `PageToken.cs` (pagination), `KeyOrdering.cs`, `ResourceEndpointSelector`, `DefaultResourceTypeResolver`.

## ENTRY POINTS

- `Extensions/SchemataBuilderExtensions.cs`: `UseResource(...)` returns `SchemataResourceBuilder`.
- Per-resource: `builder.Use<TEntity,TRequest,TDetail,TSummary>(endpoints?, configure?)`.
- Toggles: `WithAuthorization`, `WithoutCreateValidation`, `WithoutUpdateValidation`, `WithoutFreshness`.
- `.MapHttp()` (`Resource.Http`): `ResourceController`, `ResourceMethodController` (AIP-136), conventions, feature providers. Restrict per-resource: `Use<...>(r => r.MapHttp())`.
- `.MapGrpc()` (`Resource.Grpc`): code-first `ResourceService`, binder, protobuf-net `RuntimeTypeModelConfigurator`, `FileDescriptorBridge`.

## PIPELINE ORDER

Verified ordering facts (full sequence: `docs/documents/resource/create-pipeline.md`):

- Sanitization runs BEFORE validation — validators never see server-managed fields stripped from client input.
- Anonymous/authorize advisor pairs anchor ordering: `AdviceCreateRequestAuthorize.DefaultOrder = AdviceCreateRequestAnonymous.DefaultOrder + 10_000_000`.
- `Internal/ResourcePipelineRunner.cs` drives the chain per verb.

## GOTCHAS

- Sanitization runs BEFORE validation. Validators never see server-managed fields clients shouldn't supply (`docs/documents/resource/create-pipeline.md`).
- Dry-run after validation passes raises `NoContentException` without persisting.
- Expressions package gotcha, surfaced through this package's filtering: AIP-160 (`aip`) and CEL (`cel`) compilers, pushdown planning + residual scan. AIP-160 inner wildcards (`A*B`) are rejected.
- Error model deliberately more specific than `google.rpc.Code`. `ALREADY_EXISTS` omitted per AIP-211 (`SchemataResourceErrors` lives in Schemata.Common).
- Canonical docs: `docs/documents/resource/**`.

## DEPS

Heaviest cross-consumer in the repo: Caching.Skeleton, Core, Entity.Repository, Expressions.Skeleton (AIP-160/CEL), Mapping.Skeleton, Scheduling.Skeleton, Security.Skeleton, Security.Foundation.
