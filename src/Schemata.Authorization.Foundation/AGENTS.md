# Schemata.Authorization.Foundation

## OVERVIEW

OAuth 2.0 / OpenID Connect server. 120 C# files; ~47 in `Advisors/`. Sits on `Schemata.Authorization.Skeleton` (entities, manager interfaces, endpoint contracts, advisor contracts) and bridges to ASP.NET Identity via `Schemata.Authorization.Identity` (activate with `UseIdentity()` on the authorization builder).

## STRUCTURE

```
Features/         SchemataAuthorizationFeature<TApp,TAuth,TScope,TToken>;
                  orders flow features by flow.Order
Controllers/      ConnectController.*.cs (partial per endpoint)
Handlers/         DiscoveryHandler + per-flow handlers
Managers/         Schemata*Manager (Application/Authorization/Scope/Token)
Services/         TokenService, ClientAuthenticationService,
                  PairwiseSubjectTranslator, SubjectIdentifierService,
                  BackChannelLogoutService, FrontChannelLogoutService,
                  LogoutSessionHelper, ResponseModeService,
                  TokenCleanupJob, BackChannelLogoutJob
Authentication/   SchemataAuthenticationHandler (Bearer),
                  SchemataAuthorizationCodeHandler (Code);
                  CodeFlowOptions enforces PKCE downgrade protection
Binding/          OAuthRequestBinderProvider + OAuthQuery/Form binders
Filters/          OAuthExceptionFilter, NoCacheResponseAttribute
Advisors/         ~30+ built-in Advice*.cs
```

Skeleton surface (entities, managers, service contracts): src/AGENTS.md, Authorization domain.

## ENTRY POINTS

`UseAuthorization(...)` / `UseAuthorization<TApp,TAuth,TScope,TToken>(...)` in `Extensions/SchemataBuilderExtensions.cs` returns `SchemataAuthorizationBuilder<...>`.

Flow opt-ins (in `Extensions/SchemataAuthorizationBuilderExtensions.cs`): `UseCodeFlow`, `UseClientCredentialsFlow`, `UseRefreshTokenFlow`, `UseDeviceFlow`, `UseTokenExchange`, `UseIntrospection`, `UseRevocation`, `UseUserInfo`, `UseFrontChannelLogout`, `UseBackChannelLogout`, `UseEndSession`.

Custom flows: `builder.AddFlowFeature<T>() where T : IAuthorizationFlowFeature`. The feature is ordered into `SchemataAuthorizationFeature` by `flow.Order`.

Deps: `Authorization.Skeleton`, `Schemata.Core`, `Caching.Skeleton`, `Scheduling.Skeleton`, `Security.Skeleton`, `Transport.Http`.

## ADVISOR CATALOG

Naming rule: `Advice<Topic><Aspect>` — e.g. `AdviceAuthorizePkce`, `AdviceClaimsSubject`, `AdviceDiscoveryCodeFlow`. `PermissionAdvice` is a static helper, not a chain member.

To add an advisor (root registration rule applies): implement `IAdvisor<...>`, register via `services.TryAddEnumerable(ServiceDescriptor.Scoped<IAdvisor<T>, YourAdvice>())`, drop the file in `Advisors/`. Advisors here typically `throw OAuthException` / `PermissionDeniedException` rather than returning `Block`.

Built-in topics: PKCE, scope, nonce, prompt, response-mode, pairwise, destination, permission, per-flow discovery.

## GOTCHAS

- `BackChannelLogoutJob` is best-effort by spec; failures are logged, never thrown. Don't rely on exceptions for retry.
- Authorization providers never run unless `WithAuthorization()` is invoked. Silent no-op if missed.
- Security package gotcha, surfaced through this package's claims checks: wildcard claims with two or more `*` never match (`docs/documents/security.md`).
- PKCE downgrade protection in `CodeFlowOptions` rejects token requests where PKCE was absent at the authorization step. Don't disable it.
- Canonical docs: `docs/documents/authorization.md`, `docs/guides/access-control.md`.