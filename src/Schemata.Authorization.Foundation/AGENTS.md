# Schemata.Authorization.Foundation — OAuth 2.0 / OpenID Connect Server

Implementation of an OIDC-compliant authorization server on top of `Schemata.Abstractions` + ASP.NET Core. Compliant with [OpenID Connect Core 1.0](https://openid.net/specs/openid-connect-core-1_0.html). 119 source files — the largest single package.

## Layout

```
Schemata.Authorization.Foundation/
├── SchemataAuthorizationBuilder.cs   # fluent surface for token/grant/scope/entity wiring
├── Advisors/         # IAuthorizationAdvisor implementations (token issuance hooks, claim mapping)
├── Authentication/   # ASP.NET authentication handlers + bearer/cookie scheme wiring
├── Binding/          # model binders for OAuth request shapes
├── Controllers/      # /connect/* endpoints (authorize, token, revoke, introspect, userinfo, logout)
├── Extensions/       # UseAuthorization + SchemataBuilder/IServiceCollection extension methods
├── Features/         # SchemataAuthorizationFeature (priority 450_000_000) + sub-features
├── Filters/          # MVC filters for OAuth-specific response shaping
├── Handlers/         # grant-type handlers (authorization_code, client_credentials, refresh_token, …)
├── Managers/         # client/scope/token managers backed by the repository pipeline
└── Services/         # token issuer, JWKS provider, discovery document builder
```

## Public Entry Points

- [SchemataAuthorizationBuilder.cs](SchemataAuthorizationBuilder.cs) — fluent: `WithApplication<T>()`, `WithToken<T>()`, `WithScope<T>()`, `WithAuthorization<T>()`, `SetClaimMapper<T>()`, allowed-grant toggles, lifetime configuration.
- [Extensions/SchemataBuilderExtensions.cs](Extensions/SchemataBuilderExtensions.cs) — `UseAuthorization(this SchemataBuilder, Action<SchemataAuthorizationBuilder>)`. Registers the discovery + JWKS well-known endpoints with [SchemataWellKnownFeature](../Schemata.Core/Features/SchemataWellKnownFeature.cs) and wires the OAuth controllers.
- [Features/](Features/) — `SchemataAuthorizationFeature` (priority `450_000_000`); the `Schemata.Authorization.Identity` package bridges to ASP.NET Core Identity at `450_100_000`.

## Companion Packages

- [Schemata.Authorization.Skeleton](../Schemata.Authorization.Skeleton/) — the contracts (`IApplication`, `IScope`, `IToken`, `IAuthorization`, grant + response type enums, attribute markers). Pick implementations there before customising this package.
- [Schemata.Authorization.Identity](../Schemata.Authorization.Identity/) — ties this package to `Schemata.Identity.Foundation` for user resolution against ASP.NET Core Identity.

## Conventions

- **Persistence comes through `Schemata.Entity.Repository`** — every store / manager goes through `IRepository<TEntity>` and the advisor pipeline. No raw `DbContext` use.
- **Grant types live in `Handlers/`** as one class per grant; register them via the builder. New grants extend `IGrantHandler` and slot in by handler-type discovery.
- **Discovery + JWKS** are produced by `Services/` and surfaced through `SchemataWellKnownFeature` — never bake URLs into the controller code.
- **Token formats**: JWTs are issued via `Microsoft.IdentityModel.JsonWebTokens` (pinned in [../../Directory.Packages.props](../../Directory.Packages.props#L17)); signing keys come from `Services/IJsonWebKeyProvider`.

## Anti-Patterns

- **Do NOT** add a new OAuth response shape without an `OAuthErrorResponse` mapping — the AIP error envelope is the wrong shape for OAuth ([../Schemata.Abstractions/Errors/OAuthErrorResponse.cs](../Schemata.Abstractions/Errors/OAuthErrorResponse.cs)).
- **Do NOT** issue a token outside `Services/ITokenIssuer` — bypassing it skips advisors and audit hooks.
- **Do NOT** expose a controller route that lives outside `/connect/*` or `/.well-known/*` from this package. Routing conflicts with consumer applications.
- **Do NOT** depend directly on `Schemata.Identity.*` from this package — bridging is the job of `Schemata.Authorization.Identity`.

## Notes

- The OIDC discovery document is served from `/.well-known/openid-configuration`; JWKS from `/.well-known/jwks`. Both are registered through [WellKnownOptions](../Schemata.Core/WellKnownOptions.cs).
- Controllers are picked up by the MVC pipeline via [SchemataExtensionPart](../Schemata.Core/SchemataExtensionPart.cs) — consumers do **not** need to call `AddApplicationPart` manually.
- This package depends on `Schemata.Entity.Repository`, which means even pure-API consumers transitively get the repository pipeline. That is intentional — token persistence is non-negotiable.
- Integration tests: [../../tests/Schemata.Authorization.Integration.Tests/](../../tests/Schemata.Authorization.Integration.Tests/). Unit tests: [../../tests/Schemata.Authorization.Tests/](../../tests/Schemata.Authorization.Tests/).
