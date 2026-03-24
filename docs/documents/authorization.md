# Authorization

Schemata provides an OAuth 2.0 / OpenID Connect authorization server built on OpenIddict, with a composable feature system for enabling individual flows and endpoints.

## Packages

| Package | Role |
|---|---|
| `Schemata.Authorization.Skeleton` | Entity types, stores, store resolvers, response models |
| `Schemata.Authorization.Foundation` | Feature, controller, builder, flow features |

## Entity types

All entities use `long` primary keys and implement standard Schemata interfaces (`IIdentifier`, `ICanonicalName`, `IConcurrency`, `ITimestamp`).

### SchemataApplication

Represents a registered OAuth 2.0 client application. Table: `SchemataApplications`. Canonical name: `applications/{application}`.

Key properties: `ClientId`, `ClientSecret`, `ClientType` (confidential/public), `ApplicationType` (web/native), `ConsentType` (explicit/implicit/external/systematic), `RedirectUris`, `PostLogoutRedirectUris`, `Permissions`, `Requirements`, `JsonWebKeySet`, `Settings`.

The `Name` property is backed by `ClientId`.

### SchemataAuthorization

Represents an authorization grant binding a user to an application with scopes. Table: `SchemataAuthorizations`. Contains `ApplicationId`, `Subject`, `Type` (permanent/ad-hoc), `Status` (valid/revoked), and `Scopes`.

### SchemataScope

Represents an OAuth 2.0 scope. Table: `SchemataScopes`. Contains `Description`, `Resources`, and display name localization.

### SchemataToken

Represents a token (access, refresh, authorization code, or device code). Table: `SchemataTokens`. Contains `ApplicationId`, `AuthorizationId`, `Subject`, `Type`, `Status`, `Payload`, `ReferenceId`, `ExpireTime`, `RedeemTime`.

## UseAuthorization()

```csharp
builder.UseAuthorization(
    serve:     options => { ... },  // OpenIddictServerBuilder
    integrate: options => { ... },  // OpenIddictServerAspNetCoreBuilder
    store:     options => { ... }   // OpenIddictCoreBuilder
);
```

Returns a `SchemataAuthorizationBuilder` for chaining flow configurations.

### Overloads

- `UseAuthorization()` -- uses default entity types (`SchemataApplication`, `SchemataAuthorization`, `SchemataScope`, `SchemataToken`)
- `UseAuthorization<TApplication, TAuthorization, TScope, TToken>()` -- uses custom entity types

### Feature behavior

`SchemataAuthorizationFeature` depends on `SchemataControllersFeature`. It configures:

- OpenIddict Core with custom entity types and Schemata store resolvers (`SchemataApplicationStoreResolver`, `SchemataAuthorizationStoreResolver`, `SchemataScopeStoreResolver`, `SchemataTokenStoreResolver`)
- OpenIddict Server with ASP.NET Core integration, status code pages, and optional transport security disable in development
- OpenIddict Validation with local server and ASP.NET Core integration
- Composable authorization features sorted by `Order`

Transport security requirements are automatically disabled when the hosting environment is Development or when `UseHttps()` has not been called on the builder.

## Flow features

Each flow is enabled independently via the `SchemataAuthorizationBuilder`:

### Authorization Code flow with PKCE

```csharp
builder.UseAuthorization()
       .UseCodeFlow();
```

Enables `AllowAuthorizationCodeFlow()` with `RequireProofKeyForCodeExchange()`. Configures endpoints:
- `GET /Connect/Authorize` -- authorization
- `POST /Connect/Token` -- token exchange

### Refresh Token flow

```csharp
.UseRefreshTokenFlow()
```

Enables `AllowRefreshTokenFlow()`. Uses the `/Connect/Token` endpoint.

### Client Credentials flow

```csharp
.UseClientCredentialsFlow()
```

Enables `AllowClientCredentialsFlow()`. Uses the `/Connect/Token` endpoint. No user involvement -- the client authenticates with its own credentials.

### Device Authorization flow

```csharp
.UseDeviceFlow()
```

Enables `AllowDeviceAuthorizationFlow()`. Configures endpoints:
- `/Connect/Device` -- device authorization
- `/Connect/Verify` -- end-user verification
- `/Connect/Token` -- token exchange

### Token Introspection (RFC 7662)

```csharp
.UseIntrospection()
```

Configures `/Connect/Introspect` for resource servers to validate tokens.

### Token Revocation (RFC 7009)

```csharp
.UseRevocation()
```

Configures `/Connect/Revocation` for clients to invalidate tokens.

### End Session (logout)

```csharp
.UseEndSession()
```

Configures `/Connect/Logout` for OpenID Connect logout.

### Request caching

```csharp
.UseCaching()
```

Enables request caching for authorization and end-session endpoints when the corresponding flow features are present. Reduces round-trips for authorization code and logout requests.

## ConnectController

Mounted at `~/Connect`. All endpoints are API endpoints.

### GET ~/Connect/Authorize

Handles the authorization request. Behavior depends on the application's consent type:

- **Implicit** or **External with existing authorization** -- returns a sign-in result immediately
- **Explicit with existing authorization** and no `prompt=consent` -- returns a sign-in result
- **External with no authorization** -- returns a forbidden error
- **Other cases** -- returns an `AuthorizeResponse` containing the application name and requested scopes for client-side consent UI

If the user is not authenticated, returns a challenge to redirect to the login page.

Claims included in tokens: `sub`, `email`, `phone_number`, `preferred_username`, `nickname`, and `role`. Claim destinations respect scope grants (profile, email, phone, roles).

### POST ~/Connect/Authorize

Accepts the consent form and issues an authorization code. Creates a permanent authorization to avoid future consent prompts for the same scopes.

### POST ~/Connect/Token

Handles token exchange. Supports:

- **Authorization code** / **Device code** / **Refresh token** grants -- validates the stored principal, refreshes user claims, and issues new tokens
- **Client credentials** grant -- creates a claims identity using the client application's `client_id` as the subject

### GET ~/Connect/Verify

Device flow verification. Returns a `VerifyResponse` with application name, user code, and requested scopes.

### POST ~/Connect/Verify

Accepts device flow verification and issues tokens.

### POST ~/Connect/Logout

Signs out the user and redirects to the post-logout URI.

## Typical setup

```csharp
builder.UseIdentity()
       .UseAuthorization()
       .UseCodeFlow()
       .UseRefreshTokenFlow()
       .UseEndSession()
       .UseCaching();
```
