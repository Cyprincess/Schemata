# Schemata.Authorization.Skeleton

## OVERVIEW

72 files, ~2814 LOC. **Contract-only.** Zero concrete managers, services, endpoints or advisors — every implementation lives in `Schemata.Authorization.Foundation` (own AGENTS.md). The csproj references only `Schemata.Entity.Repository`, so no ASP.NET dependency reaches consumers of the contracts.

## STRUCTURE

| Folder | Role |
|---|---|
| `Entities/` | 5 persisted OAuth/OIDC POCOs |
| `Managers/` | 4 manager interfaces, one per entity |
| `Advisors/` | ~12 advisor interfaces (extension points for the grant pipelines) |
| `Contexts/` | 7 mutable carriers that flow through an advisor chain |
| `Handlers/` | 8 abstract endpoint bases + 3 strategy interfaces (`IGrantHandler`, `IInteractionHandler`, `ITokenExchangeHandler`) |
| `Services/` | `IClientAuthentication<TApplication>` + `IClientAuthenticationService<TApplication>` |
| `Models/` | ~24 DTO records (request/response/detail/summary shapes) |
| root | `ScopeParser`, `AuthorizationResult`, `AuthorizationStatus`, `ConsentDecision`, `ILogoutNotifier`, `ISubjectProvider`, `ISubjectIdentifierService`, `IPairwiseSubjectTranslator` |

No `*Options` type ships here — all options live in Foundation.

## ENTITIES

| Entity | Traits | Notes |
|---|---|---|
| `SchemataApplication` | `IIdentifier, ICanonicalName, IDescriptive, IConcurrency, ITimestamp` | the OAuth client |
| `SchemataAuthorization` | `IIdentifier, ICanonicalName, IConcurrency, ITimestamp` | consent record; `Type` in `{ad-hoc, device, permanent}` drives reuse policy |
| `SchemataScope` | `IIdentifier, ICanonicalName, IDescriptive, IConcurrency, ITimestamp` | `Resources` holds target resource AIP names |
| `SchemataToken` | adds `IExpiration` | ONE entity for authorization-code / access / refresh / device-code, discriminated by `Type`; `Format` in `{reference, jwt, jwe}` |
| `SchemataSubjectMapping` | `IIdentifier, ICanonicalName, ITimestamp` | pairwise subject table; unique indexes on `(Application, CanonicalSubject)` and `(Application, PairwiseSubject)` |

## ADVISOR CONTRACTS

| Interface | Arity |
|---|---|
| `IAuthorizeAdvisor<TApplication>` | 1 — over `AuthorizeContext<…>` |
| `ICodeExchangeAdvisor<TApplication,TToken>` | 1 — over its context |
| `IRefreshTokenAdvisor<TApplication,TToken>` | 1 — over its context |
| `IDeviceCodeExchangeAdvisor<TApplication,TToken>` | 1 — over its context |
| `IIntrospectionAdvisor<TApplication,TToken>` | 1 — over its context |
| `IDeviceAuthorizeAdvisor<TApplication>` | 2 — `TApplication`, `DeviceAuthorizeRequest` |
| `ITokenRequestAdvisor<TApplication>` | 2 — `TApplication`, `TokenRequest` |
| `IRevocationAdvisor<TApplication,TToken>` | 3 — `TApplication`, `RevokeRequest`, `TToken` |
| `IDestinationAdvisor` | 3 — `Claim`, `HashSet<string>`, `ClaimsPrincipal` |
| `IUserInfoAdvisor` / `IDiscoveryAdvisor` / `IClaimsAdvisor` | 1, closed generics |

**Generic convention.** The `TApp, TAuth, TScope, TToken` quad. Each manager is generic over its own entity; each context carries only the entity types it touches; advisors whose context carries no entity are closed. The quad is closed once, at `SchemataAuthorizationFeature<TApp,TAuth,TScope,TToken>` in Foundation.

## GOTCHAS

- `SchemataSubjectMapping` is persisted even though the forward hash `SHA-256(sector || canonical || salt)` is deterministic. Persistence exists for salt rotation and for the reverse `pairwise → canonical` lookup, which is impossible from a one-way hash.
- `IPairwiseSubjectTranslator` passes through unknown callers, public-subject clients, empty subjects and already-resolved inputs unchanged, and returns `null` for an unrecognized pairwise value. Absence of a translation is not an error.
- `IClientAuthentication.AuthenticateAsync` is **tri-state**: returns the application on a match, `null` when the method does not apply to this request, and throws `OAuthException` when the method applies but authentication fails. This three-way contract is what makes chaining implementations safe — collapsing it to nullable breaks the chain.
- `IDestinationAdvisor`: `Continue` and `Handle` both KEEP the claim; only `Block` excludes it. `Handle` additionally short-circuits the chain.
- `CodeExchangeContext.RequireSingleUse` defaults to `true`; extension grants opt out explicitly.
- `ScopeParser` is ordinal and case-sensitive per RFC 6749 §3.3. Do not make it culture-aware.

Canonical docs: `docs/documents/authorization.md`, `docs/cookbook/oidc-server.md`.
