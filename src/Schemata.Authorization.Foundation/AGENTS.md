# Schemata.Authorization.Foundation

OAuth 2.0 / OpenID Connect server. Largest single project in the repo (119 files / 8.5k lines). Implements OIDC Core 1.0 plus PKCE, Device Flow, Token Exchange, Token Introspection, Token Revocation, RP-Initiated Logout, and Back-Channel Logout.

## Layout

```
Advisors/        Pre/post-commit advisors over the authorization advisor pipeline
Authentication/  Token authentication handlers and schemes
Binding/         Model binding for OAuth form/query parameters
Controllers/     OAuth/OIDC endpoints (Token, Authorize, Device, EndSession, Introspect, Revoke, Profile, well-known)
Extensions/      SchemataBuilder.UseAuthorization(), policy/auth scheme wiring
Features/        SchemataAuthorizationFeature<TApp,TAuth,TScope,TToken> (Priority 450_000_000)
Filters/         Action filters guarding endpoints
Handlers/        Per-grant-type token handlers (authorization_code, client_credentials, refresh_token, device_code, token-exchange)
Managers/        Application/Authorization/Scope/Token managers (CRUD over the Skeleton stores)
Services/        BackChannelLogoutService, signing services, cleanup services
```

Endpoint paths are fixed by `SchemataConstants.Endpoints` (`/Connect/Token`, `/Connect/Authorize`, ...). Wire values come from `SchemataConstants` - never inline string literals for grant types, claims, errors, or parameters.

## Activation

```csharp
schema.UseAuthorization<TApp, TAuth, TScope, TToken>(opts => { ... });
```

Companion package `Schemata.Authorization.Identity` adds `UseIdentity()` on the auth sub-builder to bind ASP.NET Core Identity stores.

## Rules

- Spec compliance is non-negotiable. The README's OIDC Core 1.0 statement and the inline `<seealso>` RFC references in [SchemataConstants.cs](file:///D:/source/repos/Cyprin/Schemata/src/Schemata.Abstractions/SchemataConstants.cs) are the source of truth.
- Use the per-grant handler abstraction; do not put grant-type branching inside controllers.
- The consent advisor distinguishes `AuthorizationTypes.AdHoc` / `Device` / `Permanent`; do not collapse them. Device anchors are intentionally not reusable for silent consent because the verifying user agent differs from the requesting device.
- Back-channel logout depends on `Schemata.Scheduling.Foundation`. Calling `UseAuthorization()` without `UseScheduling()` raises `FAILED_PRECONDITION` from [Services/BackChannelLogoutService.cs](file:///D:/source/repos/Cyprin/Schemata/src/Schemata.Authorization.Foundation/Services/BackChannelLogoutService.cs).
- Permission entries use the `PermissionPrefixes` namespacing (`g:`, `s:`, `e:`). Do not store raw strings.
- Discovery and JWKS paths are relative (`openid-configuration`, `jwks`); routing is delegated to ASP.NET via the wellknown sub-feature.
