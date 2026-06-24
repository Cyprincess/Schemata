# Schemata.Abstractions

Shared cross-package contracts. **No runtime logic.** Pure types, interfaces, constants, attributes, and base records consumed by every other `src/` project.

## Layout

```
Advisors/        Advisor-pipeline contracts shared across subsystems
Entities/        Marker interfaces (ISoftDelete, IConcurrent, ITimestamped, IOwned, ...)
Errors/          Google-API-style error model types
Exceptions/      Framework exception hierarchy
Json/            Shared converters/resolvers used by Foundation packages
Modular/         Module discovery contracts (IModule, [Module])
Resource/        Resource-layer contracts surfaced through Foundation/Http/Grpc
xlf/             Localized resource fallbacks
```

Top-level: [IFeature.cs](file:///D:/source/repos/Cyprin/Schemata/src/Schemata.Abstractions/IFeature.cs) (the `Order`/`Priority` contract) and [SchemataConstants.cs](file:///D:/source/repos/Cyprin/Schemata/src/Schemata.Abstractions/SchemataConstants.cs) (the canonical constants table).

## `SchemataConstants` Anchors

`SchemataConstants` is the central registry. Add new spec-defined strings here, not in foundations. Nested classes match a single spec/concept each:

- `Orders` - `Base=100M`, `Extension=400M`, `Max=900M`
- OAuth/OIDC: `GrantTypes`, `Claims`, `ClaimDestinations`, `OAuthErrors`, `PkceMethods`, `PromptValues`, `ClientAuthMethods`, `ClientTypes`, `ConsentTypes`, `ApplicationTypes`, `AuthorizationTypes`, `Endpoints`, `Parameters`, `EventTypes`
- JOSE: `EncryptionAlgorithms`, `ContentEncryptionAlgorithms`
- Google AIP: `ErrorCodes`, `ErrorReasons`, `FieldReasons`
- Other: `FlowEngines`, `InteractionTypes`, `Keys`, `PermissionPrefixes`, `PreconditionSubjects`, `Principals`

Each constant docs the originating RFC / spec with `<seealso>`.

## Rules

- Do not introduce concrete runtime logic here. If you need DI registration, put it in a Foundation package and depend on this one.
- Constants are wire values; never localize, never trim, never reformat.
- Marker interfaces in `Entities/` are read by repository advisors (see [src/Schemata.Entity.Repository/AGENTS.md](file:///D:/source/repos/Cyprin/Schemata/src/Schemata.Entity.Repository/AGENTS.md)); changing their members ripples across every entity project.
- `IFeature` is the lowest-common contract; do not add lifecycle hooks here - those belong on `ISimpleFeature` in `Schemata.Core`.
