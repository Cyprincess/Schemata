# Authorization

Schemata provides an OAuth 2.0 / OpenID Connect authorization server with a composable feature system for enabling individual flows and endpoints.

## Packages

| Package | Role |
|---|---|
| `Schemata.Authorization.Skeleton` | Entity types, stores, store resolvers, advisor interfaces, request/response models, manager interfaces |
| `Schemata.Authorization.Foundation` | Feature, controller, handlers, advisor implementations, model binding, token service, client authentication |

## Entity types

All entities use `long` primary keys and implement standard Schemata interfaces (`IIdentifier`, `ICanonicalName`, `IConcurrency`, `ITimestamp`).

### SchemataApplication

Represents a registered OAuth 2.0 client application. Table: `SchemataApplications`. Canonical name: `applications/{application}`.

Key properties: `ClientId`, `ClientSecret`, `ClientType` (confidential/public), `ApplicationType` (web/native), `ConsentType` (explicit/implicit/external), `RedirectUris`, `PostLogoutRedirectUris`, `Permissions`, `JsonWebKeySet`, `Settings`.

The `Name` property is backed by `ClientId`.

### SchemataAuthorization

Represents an authorization grant binding a user to an application with scopes. Table: `SchemataAuthorizations`. Contains `ApplicationId`, `Subject`, `Type` (permanent/ad-hoc), `Status` (valid/revoked), and `Scopes`.

### SchemataScope

Represents an OAuth 2.0 scope. Table: `SchemataScopes`. Contains `Description`, `Resources`, and display name localization.

### SchemataToken

Represents a token (access, refresh, authorization code, or device code). Table: `SchemataTokens`. Contains `ApplicationId`, `AuthorizationId`, `Subject`, `Type`, `Status`, `Payload`, `ReferenceId`, `ExpireTime`, `RedeemTime`.

## UseAuthorization()

```csharp
builder.UseAuthorization(configure: options => { ... });
```

Returns a `SchemataAuthorizationBuilder` for chaining flow configurations.

### Overloads

- `UseAuthorization(Action<SchemataAuthorizationOptions>?)` -- uses default entity types (`SchemataApplication`, `SchemataAuthorization`, `SchemataScope`, `SchemataToken`)
- `UseAuthorization<TApplication, TAuthorization, TScope, TToken>(Action<SchemataAuthorizationOptions>?)` -- uses custom entity types

### Feature behavior

`SchemataAuthorizationFeature` depends on `SchemataAuthenticationFeature`, `SchemataControllersFeature`, and `SchemataWellKnownFeature`. It configures:

- Managers (`IApplicationManager<T>`, `IAuthorizationManager<T>`, `IScopeManager<T>`, `ITokenManager<T>`) with Schemata entity stores
- Bearer authentication scheme for token validation and issuance
- Token endpoint advisors (client authentication, grant permission, scope validation)
- Claims and destination advisors conditional on allowed scopes
- Composable authorization flow features sorted by `Order`
- Background token cleanup and back-channel logout queue

## Typical setup

```csharp
builder.UseIdentity()
       .UseAuthorization()
       .UseCodeFlow()
       .UseRefreshTokenFlow()
       .UseEndSession();
```

## Flows

Each flow is enabled independently via the `SchemataAuthorizationBuilder`. All endpoints are mounted under `~/Connect`. The authorization module uses the [advice pipeline](core/advice-pipeline.md) throughout; for the complete interface catalog, see [Authorization advisors](core/advice-pipeline.md#authorization-advisors).

### User interaction

Schemata does not ship UI for consent, device verification, or logout. These pages are implemented by the application -- a SPA or plain JavaScript page is recommended, but any frontend that can call the JSON APIs will work.

Flows that require user interaction use a common pattern:

1. The server creates a temporary **interaction token** (`SchemataToken`) and redirects to a configurable URI with a `code` parameter.
2. The UI retrieves interaction details via `GET /Connect/Interact?code={code}&code_type={type}`.
3. The UI submits the user's decision:
   - **Approve** -- `POST /Connect/Token` with `grant_type=urn:ietf:params:oauth:grant-type:token-exchange` and `subject_token={code}`.
   - **Deny** -- `DELETE /Connect/Interact?code={code}&code_type={type}`.

Token type URIs for `code_type` and `subject_token_type`:

| Flow | Token type URI |
|---|---|
| Authorization consent | `urn:schemata:authorization:token-type:interaction` |
| Device verification | `urn:schemata:authorization:token-type:user-code` |
| Logout | `urn:schemata:authorization:token-type:logout` |

`InteractionHandler` dispatches to `IInteractionHandler` implementations by code type. The `GET` endpoint returns an `InteractionResponse` containing application details, requested scopes, and (for authorization) the original request. The `DELETE` endpoint revokes the interaction token.

Configuration in `SchemataAuthorizationOptions`:

| Property | Purpose |
|---|---|
| `InteractionUri` | Consent/verification page (e.g. `https://auth.example.com/interact`) |
| `DeviceVerificationUri` | Device code entry page (e.g. `https://auth.example.com/device`) |
| `LogoutUri` | Logout page for front/back-channel notifications |

### Token endpoint (shared pipeline)

`POST /Connect/Token` -- `TokenHandler` dispatches to `IGrantHandler` implementations by `grant_type`. Each flow feature registers its own `IGrantHandler`.

`IGrantHandler` -- the extension point for grant types:

```csharp
public interface IGrantHandler
{
    string GrantType { get; }

    Task<AuthorizationResult> HandleAsync(
        TokenRequest request, ClaimsPrincipal? user,
        Dictionary<string, List<string>>? headers, CancellationToken ct);
}
```

`ITokenRequestAdvisor` runs for every grant type before the grant-specific handler. Registered by `UseAuthorization()`:

| Advisor | Responsibility |
|---|---|
| `AdviceTokenClientAuth` | Authenticates the client via `IClientAuthHandler` chain |
| `AdviceTokenGrantPermission` | Verifies the client has permission for the requested grant type |
| `AdviceTokenScopeValidation` | Validates requested scopes against client permissions |

Each grant handler then runs the flow's own advisor pipeline, followed by `IClaimsAdvisor` for claims enrichment.

### Authorization Code flow with PKCE

```csharp
.UseCodeFlow()
```

Endpoints: `GET|POST /Connect/Authorize`, `POST /Connect/Token`.

**Authorization endpoint** -- `AuthorizeHandler` processes the authorize request:

Runs `IAuthorizeEndpointAdvisor`:

| Advisor | Responsibility |
|---|---|
| `AdviceAuthorizeClientAndRedirect` | Resolves application by `client_id` and validates `redirect_uri` |
| `AdviceAuthorizeResponseMode` | Validates response mode safety |
| `AdviceAuthorizeGrantPermission` | Verifies `authorization_code` grant permission |
| `AdviceAuthorizeScopeValidation` | Validates requested scopes |
| `AdviceAuthorizePkce` | Enforces PKCE requirement per client or global setting |
| `AdviceAuthorizeNonce` | Enforces nonce for `id_token` response types |
| `AdviceAuthorizePrompt` | Handles `prompt` parameter (none, login, consent) |
| `AdviceAuthorizeConsent` | Evaluates consent based on application consent type and existing authorizations |
| `AdviceAuthorizeAutoApproveSignIn` | Auto-approves and issues sign-in result when consent is already granted |

If auto-approved (`Handle`), issues a sign-in result directly. Otherwise, creates an interaction token and redirects to the consent UI.

**Consent interaction** -- `AuthorizeInteractionHandler` (`IInteractionHandler`, code type: interaction) retrieves the stored authorize request for the consent UI and handles user denial.

**Token exchange** -- `AuthorizationCodeHandler` (`IGrantHandler`, grant type: `authorization_code`) exchanges the authorization code for tokens:

1. Validates code status, expiry, and payload integrity
2. Deserializes the stored `AuthorizeRequest` and matches `client_id` / `redirect_uri`
3. Runs `ICodeExchangeAdvisor` (receives the application, current `TokenRequest`, original `AuthorizeRequest`, and code token):

| Advisor | Responsibility |
|---|---|
| `AdviceCodeExchangePkce` | Verifies `code_verifier` against the stored code challenge |

4. Revokes the code (one-time use per RFC 9700 §2.1.2)
5. Runs `IClaimsAdvisor` and returns a sign-in result

### Client Credentials flow

```csharp
.UseClientCredentialsFlow()
```

Endpoint: `POST /Connect/Token`.

`ClientCredentialsHandler` (`IGrantHandler`, grant type: `client_credentials`). No user involvement -- the `client_id` is used as the subject. Uses the shared `ITokenRequestAdvisor` pipeline; no flow-specific advisors.

### Refresh Token flow

```csharp
.UseRefreshTokenFlow()
```

Endpoint: `POST /Connect/Token`.

`RefreshTokenHandler` (`IGrantHandler`, grant type: `refresh_token`). After the shared `ITokenRequestAdvisor` pipeline, runs:

`IRefreshTokenAdvisor` -- refresh token validation:

| Advisor | Responsibility |
|---|---|
| `AdviceRefreshTokenValidation` | Validates token type, status, expiry, and client ownership |

`IRefreshScopeAdvisor` (registered by `UseAuthorization()`) -- scope narrowing per RFC 6749 §6:

| Advisor | Responsibility |
|---|---|
| `AdviceRefreshScopeValidation` | Ensures requested scope is a subset of the original grant |

The consumed refresh token is revoked (rotation per RFC 9700 §2.2.2).

### Device Authorization flow

```csharp
.UseDeviceFlow()
```

Endpoints: `POST /Connect/Device`, `POST /Connect/Token`.

**Device authorization** -- `DeviceAuthorizeHandler` generates device and user codes. Runs `IDeviceAuthorizeAdvisor`:

| Advisor | Responsibility |
|---|---|
| `AdviceDeviceEndpointPermission` | Validates endpoint permission (RFC 8628 §3.1) |
| `AdviceDeviceAuthorizeGrantPermission` | Validates device_code grant type permission |
| `AdviceDeviceAuthorizeScopeValidation` | Validates requested scopes |

**Device verification** -- `DeviceInteractionHandler` (`IInteractionHandler`, code type: user_code) retrieves device code details for the verification UI and handles user denial.

**Token exchange** -- `DeviceCodeHandler` (`IGrantHandler`, grant type: `urn:ietf:params:oauth:grant-type:device_code`). `UseDeviceFlow()` also adds `AdviceDeviceCodePolling` to the `ITokenRequestAdvisor` pipeline to throttle polling per RFC 8628 §3.5. After the shared pipeline, runs `IDeviceCodeExchangeAdvisor`:

| Advisor | Responsibility |
|---|---|
| `AdviceDeviceCodeExchangeValidation` | Validates device code type, ownership, expiry, and authorization status |

### Token Exchange

Automatically enabled by `UseCodeFlow()` and `UseDeviceFlow()`. Can also be enabled explicitly via `.UseTokenExchange()`.

Endpoint: `POST /Connect/Token`.

`TokenExchangeHandler` (`IGrantHandler`, grant type: `urn:ietf:params:oauth:grant-type:token-exchange`) dispatches to `ITokenExchangeSubHandler` implementations by `subject_token_type`:

```csharp
public interface ITokenExchangeSubHandler
{
    string SubjectTokenType { get; }

    Task<AuthorizationResult> HandleAsync(
        TokenRequest request, ClaimsPrincipal? user,
        SchemataToken interaction, CancellationToken ct);
}
```

Built-in sub-handler: `InteractionTokenExchangeHandler` (subject token type: `urn:schemata:authorization:token-type:interaction`) -- completes the authorization code flow after user consent by exchanging the interaction token for access/refresh tokens.

`UseDeviceFlow()` adds `DeviceCodeExchangeHandler` (subject token type: `urn:schemata:authorization:token-type:user-code`) as a second sub-handler. It marks the device code as authorized and revokes the user code token, returning `204 No Content`. The device then obtains tokens by polling with `grant_type=urn:ietf:params:oauth:grant-type:device_code`.

`ITokenExchangeAdvisor` -- token exchange validation:

| Advisor | Responsibility |
|---|---|
| `AdviceTokenExchangeValidation` | Validates token status and expiry |
| `AdviceTokenExchangeGrantPermission` | Verifies client has `token-exchange` grant permission |

### Token Introspection (RFC 7662)

```csharp
.UseIntrospection()
```

Endpoint: `POST /Connect/Introspect`.

`IntrospectionHandler` validates and returns token metadata. Uses `token_type_hint` to optimize lookup order (JWT vs. reference token).

`IIntrospectionRequestAdvisor` -- client authentication:

| Advisor | Responsibility |
|---|---|
| `AdviceIntrospectionClientAuth` | Authenticates the client |

`IIntrospectionAdvisor` allows customization of the introspection response. Per RFC 7662 §2.1, invalid or unauthorized requests return `active=false` (not an error).

### Token Revocation (RFC 7009)

```csharp
.UseRevocation()
```

Endpoint: `POST /Connect/Revoke`.

`RevocationHandler` revokes tokens. Uses `token_type_hint` to optimize lookup order. Validates client ownership before revocation.

`IRevocationRequestAdvisor` -- client authentication:

| Advisor | Responsibility |
|---|---|
| `AdviceRevocationClientAuth` | Authenticates the client |

`IRevocationAdvisor` provides a per-token hook before revocation is committed.

### End Session (OIDC RP-Initiated Logout)

```csharp
.UseEndSession()
```

Endpoint: `GET|POST /Connect/EndSession`.

`EndSessionHandler` processes logout. After validation, sends front-channel and back-channel logout notifications, then signs out the user.

`IEndSessionRequestAdvisor` -- request validation:

| Advisor | Responsibility |
|---|---|
| `AdviceEndSessionClientLookup` | Validates `id_token_hint`, resolves client application |
| `AdviceEndSessionRedirectUri` | Validates `post_logout_redirect_uri` against registered URIs |

`IEndSessionAdvisor` runs after validation, before sign-out.

**Logout interaction** -- `LogoutInteractionHandler` (`IInteractionHandler`, code type: logout) manages the logout interaction when front-channel or back-channel notifications are needed.

### Claims and token issuance

Registered by `UseAuthorization()`. Run by all grant handlers after flow-specific processing.

`IClaimsAdvisor` -- claims enrichment before token issuance:

| Advisor | Responsibility |
|---|---|
| `AdviceAudienceClaims` | Adds `client_id` as an audience claim per OIDC Core §2 |
| `AdviceSubjectClaims` | Enriches claims from `ISubjectProvider` (conditional on Identity being enabled) |

`IDestinationAdvisor` -- determines which tokens (access/identity) receive each claim:

| Advisor | Condition |
|---|---|
| `AdviceSubjectClaimDestination` | Always |
| `AdviceProfileClaimDestination` | `profile` scope permitted |
| `AdviceEmailClaimDestination` | `email` scope permitted |
| `AdvicePhoneClaimDestination` | `phone` scope permitted |
| `AdviceAddressClaimDestination` | `address` scope permitted |

`ITokenAdvisor` runs as a final hook before token issuance.

### Discovery

`GET /.well-known/openid-configuration` and `GET /.well-known/jwks`.

`DiscoveryHandler` builds the OIDC discovery document and exposes the public JWKS. These endpoints are registered as Minimal API routes via `SchemataWellKnownFeature`, independent of the MVC controller.

`IDiscoveryAdvisor` populates the document. `UseAuthorization()` registers `AdviceDiscoveryBase` (token endpoint, JWKS URI). Each flow feature adds its own entries (grant types, endpoints, capabilities).

### UserInfo

`GET|POST /Connect/Profile`.

`UserInfoHandler` returns claims for the authenticated user. Filters claims to those with the `userinfo` destination.

`IUserInfoRequestAdvisor` -- validates the access token. `IUserInfoResponseAdvisor` -- customizes the response body.

### Interactions

`InteractionHandler` dispatches to `IInteractionHandler` implementations by code type URI. Each flow that requires user-facing interaction registers its own handler:

| Code type | Handler | Registered by |
|---|---|---|
| interaction | `AuthorizeInteractionHandler` | `UseCodeFlow()` |
| user_code | `DeviceInteractionHandler` | `UseDeviceFlow()` |
| logout | `LogoutInteractionHandler` | `UseEndSession()` |

`IInteractionHandler` provides two operations: `GetDetailsAsync` (retrieve interaction details for the UI) and `DenyAsync` (user-initiated denial).

### Model binding

`OAuthRequestBinder<T>` binds all OAuth request types generically. For each `string?` property on the model, the binder converts the property name from PascalCase to snake_case (e.g. `CodeChallengeMethod` → `code_challenge_method`) and reads the value from the form body (preferred) or query string (fallback).

Supported request models: `AuthorizeRequest`, `TokenRequest`, `DeviceAuthorizeRequest`, `EndSessionRequest`, `IntrospectRequest`, `RevokeRequest`.

### Client authentication

Client credentials can be supplied via `client_secret_post` (form body) or `client_secret_basic` (HTTP Basic `Authorization` header). The `IClientAuthHandler` interface allows additional methods:

```csharp
public interface IClientAuthHandler
{
    string Method { get; }

    Task<(string? clientId, string? clientSecret)> ExtractCredentialsAsync(
        string? clientId,
        string? clientSecret,
        Dictionary<string, List<string>>? headers,
        CancellationToken ct);
}
```

Handlers are tried in registration order. HTTP headers are collected by the controller and passed as a dictionary through the advisor pipeline.

### Token issuance

`SchemataAuthorizationHandler` processes `AuthorizationResult.SignIn` results. It:

1. Runs the `IDestinationAdvisor` pipeline to determine which claims go to access tokens vs. identity tokens
2. Runs the `ITokenAdvisor` pipeline for final adjustments
3. Issues JWT access tokens and optional refresh tokens via `TokenService`

### Managers

Schemata provides its own manager interfaces in `Schemata.Authorization.Skeleton.Managers`:

| Interface | Purpose |
|---|---|
| `IApplicationManager<T>` | Client application CRUD, permission checks, secret validation |
| `IAuthorizationManager<T>` | Authorization grant CRUD |
| `IScopeManager<T>` | Scope CRUD and resource resolution |
| `ITokenManager<T>` | Token CRUD, revocation, reference ID lookup |
