# Authorization

`Schemata.Authorization.Foundation` is a hand-rolled OAuth 2.0 / OpenID Connect authorization
server. It builds on `Microsoft.IdentityModel` for key material and JWT handling but pulls in no
external server framework. The core feature is generic over four entity types — `TApp`, `TAuth`,
TScope` — and runs at priority 460,000,000. Flows are opt-in: `UseAuthorization()`
registers the core, and each `Use*Flow` / `Use*` call on the returned builder adds one
`IAuthorizationFlowFeature`.

## Where the code lives

| Package                             | Key files                                                                                                                                                                                                                                                                        |
| ----------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Authorization.Skeleton`   | `Entities/{SchemataApplication,SchemataAuthorization,SchemataScope}.cs`, `Advisors/`, `Contexts/`, `Handlers/`, `Managers/`, `Services/IClientAuthentication.cs`, `ISubjectProvider.cs`                                                                                            |
| `Schemata.Security.Skeleton`        | `Entities/{SchemataToken,SchemataSecurity}.cs`, `Services/{ITokenStore,ISecurityStore,ISecretVerifier,SchemataKeyMaterial}.cs`                                                                                                                                                     |
| `Schemata.Security.Foundation`      | `Stores/{RepositoryTokenStore,CacheTokenStore,SecurityStore}.cs`, `Services/{SecretVerifier,SecurityKeyMaterialExtensions}.cs`                                                                                                                                                     |
| `Schemata.Authorization.Foundation` | `Extensions/SchemataBuilderExtensions.cs` (`UseAuthorization`), `Extensions/SchemataAuthorizationBuilderExtensions.cs` (flow methods), `Features/`, `Controllers/ConnectController*.cs`, `Authentication/SchemataAuthorizationOptions.cs`, `Managers/`, `Services/`, `Advisors/` |
| `Schemata.Authorization.Identity`   | `Features/SchemataAuthorizationIdentityFeature.cs`, `IdentitySubjectProvider.cs`, `Advisors/AdviceClaimsSubject.cs`, the `UseIdentity()` builder extension                                                                                                                       |

## Enabling the server

```csharp
builder.UseSchemata(schema => {
    schema.UseIdentity();

    schema.UseSecurity();                   // security rows, secret verification, token stores

    schema.UseAuthorization(o => {
              o.Issuer = "https://auth.example.com";
          })
          .UseIdentity()                          // bridge in the Identity subject provider
          .UseCodeFlow()
          .UseRefreshTokenFlow()
          .UseUserInfo();
});
```

`UseAuthorization` has two overloads: a default one over `SchemataApplication`,
`SchemataAuthorization`, `SchemataScope`, `SchemataToken`, and a generic one for custom subclasses.
Both take an optional `Action<SchemataAuthorizationOptions>`, store it, map the discovery and JWKS
endpoints into the well-known pipeline, add `SchemataAuthorizationFeature<...>`, and return a
`SchemataAuthorizationBuilder<TApp, TAuth, TScope>` for chaining.

## Resource management surface

`SchemataAuthorizationBuilder<TApp,TAuth,TScope>` implements `IResourceBuilder`. Application, Scope, and Token management resources are exposed only after an explicit transport activation:

```csharp
schema.UseSecurity();
schema.UseAuthorization()
      .WithAuthentication("Bearer")
      .WithAuthorization()
      .MapHttp();
```

The shared Security extensions configure only this resource management surface. `MapHttp()` and `MapGrpc()` activate the concrete Authorization transport features. The `/Connect` OAuth and OpenID Connect endpoints retain their protocol pipeline.

## What the core feature registers

`SchemataAuthorizationFeature<TApp, TAuth, TScope>` (`Priority = Orders.Extension +
60_000_000 = 460_000_000`) depends on `SchemataAuthenticationFeature`,
`SchemataTransportHttpFeature`, and `SchemataWellKnownFeature`. `ConfigureServices`:

- Validates `SchemataAuthorizationOptions` in `PostConfigure`: the `Issuer` is required, and a
  blank value throws `InvalidOperationException`.
- Collects the registered `IAuthorizationFlowFeature` instances, sorts them by `Order`, and calls
  `ConfigureServices` on each — this is how flow methods contribute their handlers and advisors.
- Adds the controller as a `SchemataApplicationPart` and inserts `OAuthRequestBinderProvider` at
  the front of the MVC model-binder chain so OAuth form/query parameters bind to the OAuth model
  types instead of the default MVC binders.
- Registers three scoped managers — `IApplicationManager<TApp>`, `IScopeManager<TScope>`, and
  `IAuthorizationManager<TAuth>` — and consumes the unified token stores over the concrete
  `SchemataToken` (`AddTokenStores()` registers the repository-backed `ITokenStore<SchemataToken>`
  and the `nonce`, `jti`, and `rate-slot` keyed slots served by the cache-backed store).
- Registers client authentication: `ClientSecretBasicAuthentication<TApp>`,
  `ClientSecretPostAuthentication<TApp>`, `ClientSecretJwtAuthentication<TApp>`, and
  `PrivateKeyJwtAuthentication<TApp>` as `IClientAuthentication<TApp>`, plus
  `IClientAuthenticationService<TApp>`.
- Registers the advisor families (see below), `DiscoveryHandler<TScope>`, `TokenService`,
  `IAuthorizationSignInService`, and `ISubjectIdentifierService`. The sign-in service issues either
  a transport-neutral `TokenResponse` or authorization callback parameters.
- Adds two authentication schemes via `AddAuthentication()`: `BearerScheme`
  (`SchemataAuthenticationHandler<TApp>`) and `CodeScheme`
  (`SchemataAuthorizationCodeHandler<TApp>`). Connect endpoints render issued responses in
  the controller; the schemes are thin compatibility adapters over the same issuer.
- Registers `TokenCleanupJob` and schedules it through the Scheduling job model — see
  below.

## Endpoints

`ConnectController` is routed at `~/Connect`. The actions a deployment actually serves depend on
which flow methods are enabled, but the routes are fixed:

| Method                    | Route                 | Action                                                | Spec                                                          |
| ------------------------- | --------------------- | ----------------------------------------------------- | ------------------------------------------------------------- |
| `GET` / `POST`            | `/Connect/Authorize`  | `AuthorizeGet` / `AuthorizePost`                      | RFC 6749 §3.1, Authorization Endpoint                         |
| `POST`                    | `/Connect/Token`      | `Token`                                               | RFC 6749 §3.2, Token Endpoint                                 |
| `POST`                    | `/Connect/Device`     | `Device`                                              | RFC 8628 §3.1, Device Authorization Request                   |
| `GET` / `POST` / `DELETE` | `/Connect/Interact`   | `Interact` / `ApproveInteraction` / `DenyInteraction` | consent interaction                                           |
| `POST`                    | `/Connect/Introspect` | `Introspect`                                          | RFC 7662 §§2.1–2.2, Introspection Request and Response        |
| `POST`                    | `/Connect/Revoke`     | `Revoke`                                              | RFC 7009 §§2.1–2.2, Revocation Request and Response            |
| `GET` / `POST`            | `/Connect/Profile`    | `Profile` (bearer-authorized)                         | OpenID Connect Core 1.0 §5.3, UserInfo Endpoint               |
| `POST` / `GET` | `/Connect/Register` | `Register` / `RegisterRead` | OIDC Dynamic Client Registration 1.0 §§3.1-3.3               |
| `GET` / `POST`            | `/Connect/EndSession` | `EndSessionGet` / `EndSessionPost`                    | OpenID Connect RP-Initiated Logout 1.0 §2, RP-Initiated Logout |

`IAuthorizationSignInService` owns transport-neutral protocol issuance. `ConnectController` renders
endpoint token responses as JSON and callback parameters through `ResponseModeService` as query,
fragment, or `form_post`. `IAuthorizationSignInHttpWriter` renders only direct compatibility-scheme
sign-ins. Endpoint handlers remain transport-neutral and do not access `HttpContext`.

`GET /.well-known/openid-configuration` is the configuration path required by OpenID Connect
Discovery 1.0 §4, Obtaining OpenID Provider Configuration Information. `GET /.well-known/jwks` is
Schemata's JWK Set route. Discovery §3, OpenID Provider Metadata, requires the `jwks_uri` metadata
field and publishes the provider-selected URL through that field; it does not prescribe a fixed JWK
Set path. Both routes are mapped through `WellKnownOptions` (the `SchemataWellKnownFeature`
pipeline), backed by `DiscoveryHandler<TScope>`. Each `IDiscoveryAdvisor` contributes a slice of the
discovery document, so the advertised grant types and endpoints reflect exactly which flows are
enabled.

## Interaction redirect

`/Connect/Authorize` never collects credentials itself. When the request needs a human — no cookie
session, `prompt=login`, or a consent decision that is not already granted — the handler mints an
interaction token and returns `302` to
`{SchemataAuthorizationOptions.InteractionUri}?code={reference}&code_type={type}`. The interaction
page signs the user in, then posts the code back to `/Connect/Interact` to resume the authorize
request. `AuthorizationCodeFlowFeature` checks `InteractionUri` once at startup — blank values and
values that fail `Uri.TryCreate(..., UriKind.Absolute, ...)` both throw `InvalidOperationException`,
so `AuthorizeHandler` builds the redirect without re-checking it. `UseDeviceFlow()` validates
`DeviceVerificationUri` the same way.

`GET /Connect/Interact` returns the original request beside the client and scope metadata, so the
page can relay request parameters into the sign-in itself: `request.acr_values` carries the
requested Authentication Context Classes (Core §3.1.2.1), and the identity login accepts them on
the login body, stamping the satisfied class as the `acr` claim — the performed level when the
request cannot be satisfied (§5.5.1.1). Token issuance re-tags that claim onto the ID token and
access token.

Two exceptions stay outside the redirect: `prompt=none` without a session raises `login_required`
per OpenID Connect Core §3.1.2.1, and `POST /Connect/Interact` from an unauthenticated caller answers
`401` rather than a redirect the XHR caller cannot follow.

The device flow reuses `/Connect/Interact` and carries its end-user verification code in the
`user_code` parameter, the name RFC 8628 §3.2 gives that value in the device authorization response
and §3.3 has the user type at the verification URI. `DeviceInteractionHandler` reads
`InteractRequest.UserCode` for every device interaction — details, approve, and deny.
`InteractRequest.Code` carries the opaque interaction reference minted by `/Connect/Authorize`.

`InteractionUri` is the authorization server's interaction page. The identity package's
`SchemataIdentityOptions.LoginUri` is a separate redirect serving cookie challenges on ordinary
`[Authorize]` endpoints — see [Identity](identity.md). Neither redirects to the other.

## Flows

Each method on `SchemataAuthorizationBuilder` adds one or more flow features. The grant types and
endpoints below are the ones the code implements:

| Builder method               | Grant type / endpoint                                             | Flow feature                                                            |
| ---------------------------- | ----------------------------------------------------------------- | ----------------------------------------------------------------------- |
| `UseCodeFlow()`              | `authorization_code` (+ PKCE), `/Connect/Authorize`               | `AuthorizationCodeFlowFeature` (+ `TokenFeature`, `InteractionFeature`) |
| `UseClientCredentialsFlow()` | `client_credentials`                                              | `ClientCredentialsFlowFeature`                                          |
| `UseRefreshTokenFlow()`      | `refresh_token`                                                   | `RefreshTokenFlowFeature`                                               |
| `UseDeviceFlow()`            | `urn:ietf:params:oauth:grant-type:device_code`, `/Connect/Device` (RFC 8628 §§3.1, 3.4) | `DeviceFlowFeature` (+ `InteractionFeature`)                            |
| `UseTokenExchange()`         | `urn:ietf:params:oauth:grant-type:token-exchange` (RFC 8693 §2.1)                       | `TokenExchangeFeature`                                                  |
| `UseJwtBearerGrant()`        | `urn:ietf:params:oauth:grant-type:jwt-bearer` (RFC 7523 §3.1; needs a trusted issuer)   | `JwtBearerGrantFeature<TApp>`                                           |
| `UseRichAuthorizationRequests()` | `authorization_details` at `/Connect/Authorize` (RFC 9396 §6; ignored when the feature is absent) | `RichAuthorizationFeature<TApp>`                              |
| `UseIntrospection()`         | `/Connect/Introspect` (RFC 7662 §§2.1–2.2)                                             | `IntrospectionFeature`                                                  |
| `UseRevocation()`            | `/Connect/Revoke` (RFC 7009 §§2.1–2.2)                                                 | `RevocationFeature`                                                     |
| `UseDynamicClientRegistration()` | `/Connect/Register` (OIDC DCR 1.0 §§3.1-3.3; registration gated by a host-supplied `IInitialAccessTokenValidator`; anonymous requests rejected with 401) | `DynamicRegistrationFeature`                                            |
| `UseUserInfo()`              | `/Connect/Profile` (OpenID Connect Core 1.0 §5.3)                                     | `UserInfoFeature`                                                       |
| `UseEndSession()`            | `/Connect/EndSession` (OpenID Connect RP-Initiated Logout 1.0 §2)                     | `EndSessionFeature`                                                     |
| `UseFrontChannelLogout()`    | front-channel logout metadata                                     | `FrontChannelLogoutFeature`                                             |
| `UseBackChannelLogout()`     | back-channel logout queue + notifier                              | `BackChannelLogoutFeature`                                              |
| `UsePairwiseSubjects()`      | pairwise `sub` projection + discovery advertisement (OIDC Core 1.0 §8)  | `PairwiseFeature<TApp>`                                                 |

`UseCodeFlow` and `UseRefreshTokenFlow` accept optional `Action<CodeFlowOptions>` /
`Action<RefreshTokenFlowOptions>` configurators. `TokenFeature` is shared: any grant that lands on
`/Connect/Token` pulls it in.

`POST /Connect/Token` dispatches by `grant_type` to the registered `IGrantHandler`. Before the
grant runs, the `ITokenRequestAdvisor<TApp>` chain validates the request:

| Advisor                                 | Checks                                                      |
| --------------------------------------- | ----------------------------------------------------------- |
| `AdviceRequestEndpointPermission<TApp>` | The client holds the `e:/Connect/Token` permission          |
| `AdviceRequestGrantPermission<TApp>`    | The client holds `g:{grant_type}`                           |
| `AdviceRequestScopeValidation<TApp>`    | Requested scopes are within the client's `s:{scope}` grants |

## Advisor families

Six advisor families extend the pipeline; all are registered via `TryAddEnumerable` and run as
ordered chains.

| Interface                                                                                                                                                                        | Generic params     | Role                                                        | Built-ins                                                                                                                                                                                                                                                                                                        |
| -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------ | ----------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `IDiscoveryAdvisor`                                                                                                                                                              | —                  | Populate the discovery document                             | `AdviceDiscoveryBase`, `AdviceDiscoveryClientAuthentication`, and `AdviceDiscoveryAcrValues` plus one per flow (`AdviceDiscoveryCodeFlow`, `AdviceDiscoveryRefreshToken`, `AdviceDiscoveryDeviceFlow`, `AdviceDiscoveryIntrospection`, `AdviceDiscoveryRevocation`, `AdviceDiscoveryUserInfo`, `AdviceDiscoveryEndSession`, …) |
| `IClaimsAdvisor`                                                                                                                                                                 | —                  | Enrich the principal before token issuance                  | `AdviceClaimsAudience`, `AdviceClaimsAuthenticationContext`, `AdviceClaimsPairwise<TApp>` (pairwise flow feature), and `AdviceClaimsSubject` (Identity bridge)                                                                                                                                                         |
| `IDestinationAdvisor`                                                                                                                                                            | —                  | Route each claim to access token, ID token, and/or UserInfo | `AdviceDestinationSubject`, `Advice{Profile,Email,Phone,Address,Role}ClaimDestination`                                                                                                                                                                                                                           |
| `ITokenRequestAdvisor<TApp>`                                                                                                                                                     | `TApp`             | Validate the token request                                  | `AdviceRequestEndpointPermission`, `AdviceRequestGrantPermission`, `AdviceRequestScopeValidation`                                                                                                                                                                                                                |
| `IAuthorizeAdvisor<TApp>`                                                                                                                                                        | `TApp`             | Validate the authorize request                              | `AdviceAuthorizeClientAndRedirect`, `AdviceAuthorizeEndpointPermission`, `AdviceAuthorizeGrantPermission`, `AdviceAuthorizeScopeValidation`, `AdviceAuthorizePkce`, `AdviceAuthorizeNonce`, `AdviceAuthorizePrompt`, `AdviceAuthorizeResponseMode`, `AdviceAuthorizeConsent`, `AdviceAuthorizeAutoApproveSignIn` |
| `ICodeExchangeAdvisor` / `IRefreshTokenAdvisor` / `IIntrospectionAdvisor` / `IRevocationAdvisor` / `IUserInfoAdvisor` / `IDeviceAuthorizeAdvisor` / `IDeviceCodeExchangeAdvisor` | `TApp` | Validate each endpoint's request                            | `AdviceCodeExchange*`, `AdviceRefreshTokenValidation`, `AdviceIntrospection*`, `AdviceRevocation*`, `AdviceUserInfoOpenIdRequirement`, `AdviceDevice*`                                                                                                                                                           |

## Permissions

A client's capabilities are a list of permission strings on `SchemataApplication.Permissions`,
prefixed per `AuthorizationConstants.PermissionPrefixes`:

| Prefix | Constant    | Example                                                           |
| ------ | ----------- | ----------------------------------------------------------------- |
| `e:`   | `Endpoint`  | `e:/Connect/Token`, `e:/Connect/Authorize`                        |
| `g:`   | `GrantType` | `g:authorization_code`, `g:client_credentials`, `g:refresh_token` |
| `s:`   | `Scope`     | `s:openid`, `s:profile`                                           |

`IApplicationManager<TApp>.HasPermissionAsync(app, permission, ct)` is the lookup the permission
advisors use.

## Audience and application bindings

`SchemataApplication.Name` aliases the OAuth `ClientId`; its canonical name is a distinct AIP-122
reference such as `applications/test-client`. `AdviceClaimsAudience` preserves an explicit
`aud` claim set. Otherwise, it mints two claims, each pre-tagged with a single destination so the
destination split routes them without further handling: the access token carries
`aud = DefaultResource ?? Issuer` (RFC 8707 §2 default resource; RFC 9068 §2.2), and the ID token
carries `aud = client_id` (OIDC Core §2), skipped when the claim set holds no client. A blank
`DefaultResource` and `Issuer` leave the access-token audience unset.

`SchemataToken.Application` and `SchemataAuthorization.Application` persist the canonical
application reference. Authorization-code and refresh-token exchange compare that value with the
resolved application's `CanonicalName` and return `invalid_grant` on a mismatch. Bearer validation
uses the stored canonical application reference as the expected JWT audience. Token issuance copies
the assembled claims and appends a new `jti` to each issued token, keeping the caller's claim list
unchanged. An auto-approved authorization stores its generated `SchemataAuthorization.CanonicalName`
in the authentication properties, which becomes the emitted token's canonical authorization
reference.

## Managers

The managers are open-generic over their entity type and take a `CancellationToken` on every
method. Key lookups:

- `IApplicationManager<TApp>`: `FindByClientIdAsync`, `ValidateRedirectUriAsync`,
  `ValidatePostLogoutRedirectUriAsync`, `HasPermissionAsync`, and the `Set*` property helpers.
- `IScopeManager<TScope>`: `FindByNameAsync`, `ListAsync`.
- `IAuthorizationManager<TAuth>`: `CreateAsync` and lifecycle queries.
- `ITokenStore<SchemataToken>`: OAuth row queries and state (`FindByReferenceIdAsync`,
  `FindByNameAsync`, `ListByParentAsync`, `ListBySessionAsync`, `CreateAsync`, `TryRedeemAsync`,
  `RevokeAsync`, `RevokeByAuthorizationAsync`, `RevokeBySessionAsync`, `PruneAsync(ct)`) plus
  key-value slot operations (`GetAsync`, `GetOrCreateAsync`, `SetAsync`, `RemoveAsync`). The
  plain slot resolves to the repository-backed store; the `nonce`, `jti`, and `rate-slot` keyed
  slots resolve to the cache-backed store.

Client credentials and assertion keys live in security rows addressed through `SecurityParents`
(`Application(app)` builds `applications/{ClientId}`, `Issuer(issuer)` returns the issuer URI)
and read through `ISecurityStore<TSecurity>`, which `UseSecurity()` registers. The shared
`ClientSecretValidator` verifies the presented secret against the client's newest valid
`password` row (`usage=authentication`) with `ISecretVerifier`; `client_secret_jwt` reads
`secret` rows; `private_key_jwt` loads JOSE material from `jwk`, `jwks`, and `jwks-uri` rows
through `ToKeyMaterialAsync`, adapted by `SecurityKeyAdapter`.
Rows persist verbatim, in plaintext at rest.

## Background jobs

Token cleanup runs through the Scheduling job model. The core feature registers
`TokenCleanupJob` through `services.AddScheduledJob<TokenCleanupJob>()` (transient
registration plus a known-only job entry) and adds a `JobRegistration` to
`SchemataSchedulingOptions.Jobs` with a
`CronSchedule("0 * * * *")` — hourly at minute 0. That extension is the registration helper for
feature authors; application code registers jobs through `WithJob<T>()`. The job calls
`ITokenStore<SchemataToken>.PruneAsync`, and the store owns its clock. This needs `SchemataSchedulingFeature` and a registered token
repository registered.

`UseBackChannelLogout()` registers `BackChannelLogoutFeature`, which wires
`BackChannelLogoutService<TApp>` as the `ILogoutNotifier`, an `HttpClient`, and a transient
`BackChannelLogoutJob`. The service builds the per-RP logout token, signs it, and triggers the job;
there is no cron schedule on it.

## Identity bridge

`Schemata.Authorization.Identity` connects the authorization server to ASP.NET Core Identity. It is
not automatic — you opt in by calling `.UseIdentity()` on the `SchemataAuthorizationBuilder`, which
adds `SchemataAuthorizationIdentityFeature` (`Priority = SchemataAuthorizationFeature<,,,>.DefaultPriority + 100_000 =
460_100_000`). The feature declares `[DependsOn(typeof(SchemataAuthorizationFeature<,,,>))]` and
`[DependsOn(typeof(SchemataIdentityFeature<,,,>))]` — open-generic type references that match any
closed instantiation. The package references `Schemata.Identity.Foundation` (not
`Schemata.Identity.Skeleton`) so the feature types are reachable. At configure time it discovers the
registered user type from
the `IUserValidator<>` descriptor, registers `IdentitySubjectProvider<TUser>` as `ISubjectProvider`,
and adds `AdviceClaimsSubject` to the `IClaimsAdvisor` chain. `IdentitySubjectProvider` projects
`sub`, `preferred_username`, `email` (+`email_verified`), `phone_number` (+`phone_number_verified`),
`nickname`, and `role` claims from the user.

## Entity types

All entities use `Guid Uid` as the primary key and carry `[PrimaryKey(nameof(Uid))]`:

| Entity                  | Table                    | Canonical name                   | Notable properties                                                                                                                                      |
| ----------------------- | ------------------------ | -------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `SchemataApplication`   | `SchemataApplications`   | `applications/{application}`     | `ClientId` (`Name` alias), `ClientType`, `ConsentType`, `RequirePkce`, `RedirectUris`, `PostLogoutRedirectUris`, `Permissions`, `BackChannelLogoutUri` |
| `SchemataAuthorization` | `SchemataAuthorizations` | `authorizations/{authorization}` | `Application` (canonical reference), `Subject`, `Type`, `Status`, `Scopes`, `CodeChallengeMethod`                                                        |
| `SchemataScope`         | `SchemataScopes`         | `scopes/{scope}`                 | `Name`, `Resources`                                                                                                                                     |
| `SchemataToken`         | `SchemataTokens`         | `tokens/{token}`                 | `Parent` (subject reference), `Application` and `Authorization` (canonical references), `Provider`, `SessionId`, `Type`, `Status`, `Format`, `ReferenceId`, `Payload`, `Value` (slot payload), `ExpireTime`; defined in `Schemata.Security.Skeleton` |
| `SchemataSubjectMapping` | `SchemataSubjectMappings` | `subjectMappings/{subjectMapping}` | `Application` (canonical reference), `CanonicalSubject`, `PairwiseSubject`, `SectorHost` |
| `SchemataSecurity`      | `SchemataSecurities`     | `securities/{security}`          | `Parent` (host-resource canonical name or issuer URI), `Kind`, `Algorithm`, `Usage`, `Kid`, `Value` (plaintext material), `Status`; defined in `Schemata.Security.Skeleton` |

## SchemataAuthorizationOptions

Signing and encryption key material is served from security rows under the issuer, and client
secrets live in rows under each application (see Managers). Lifetimes and formats have defaults:

| Property                                    | Default          | Notes                                                       |
| ------------------------------------------- | ---------------- | ----------------------------------------------------------- |
| `Issuer`                                    | —                | Required (`iss` claim, discovery base URL)                  |
| Issuer signing rows                         | —                | `SchemataSecurity` rows under `SecurityParents.Issuer(Issuer)` with `usage=signing`; the newest `valid` row signs, `valid` and `retired` rows verify |
| Issuer encryption rows                      | none             | `usage=encryption` rows; the newest `valid` row encrypts JWE output, with the row `Algorithm` as the JWE `alg` |
| `ContentEncryptionAlgorithm`                | `A256CBC-HS512`  | JWE `enc` for encrypted tokens |
| `AccessTokenFormat`                         | `Jwe`            | `Jwt`, `Jwe`, or `Reference`                                |
| `RefreshTokenFormat`                        | `Reference`      |                                                             |
| `AccessTokenLifetime` / `IdTokenLifetime`   | 1 hour           |                                                             |
| `RefreshTokenLifetime`                      | 14 days          |                                                             |
| `AuthorizationCodeLifetime`                 | 10 minutes       |                                                             |
| `DeviceCodeLifetime` / `DeviceCodeInterval` | 15 minutes / 5 s |                                                             |
| `SubjectType`                               | `Public`         | `Public` or `Pairwise`; pairwise projection requires `UsePairwiseSubjects()` and derives from the application's `SectorIdentifierUri` (or first redirect URI host) and the global `PairwiseSalt`. `PairwiseSubjectTranslator<TApp>` persists canonical-subject-to-pairwise-subject mappings in `SchemataSubjectMapping`. |
| `DeviceVerificationUri`                     | `null`           | Required by the device flow                                 |
| `BearerScheme` / `CodeScheme`               | scheme constants | Authentication scheme names                                 |
| `AcrValuesSupported`                        | empty            | Authentication Context Classes the deployment supports; advertised as the discovery `acr_values_supported` array and omitted while empty |
| `JwtBearerTrustedIssuers`                  | empty            | `jwt-bearer` grant trust anchors (RFC 7523); register each external issuer's public key through `AddJwtBearerTrustedIssuer(issuer, key)` |

`PermitResponseType(...)` and `AddJwtBearerTrustedIssuer(...)` are fluent helpers on the options object. DPoP proof configuration lives on `DPopOptions` (`SigningAlgorithms` / `ProofTimeWindow` / `NonceLifetime` defaulting to the nine RFC 7518 algorithms / 30 s / 5 min, plus `RequireAllClients`); `AddSchemataAuthorization()` registers it, and `UseDemonstratingProofOfPossession()` customizes it.

## Extension points

| Interface                                | Purpose                                                        |
| ---------------------------------------- | -------------------------------------------------------------- |
| `IAuthorizationFlowFeature`              | Add a grant type or endpoint as an ordered flow feature.       |
| `IGrantHandler`                          | Implement a token-endpoint grant.                              |
| `IClaimsAdvisor` / `IDestinationAdvisor` | Add claims and route them to tokens.                           |
| `IDiscoveryAdvisor`                      | Add discovery-document entries.                                |
| `IClientAuthentication<TApp>`            | Add a client authentication method.                            |
| `ISubjectProvider`                       | Provide the subject identifier (wired by the Identity bridge). |

## Standards compliance

Every row is judged by the full path — binding, advisor, issuance, storage, wire — never by a
CLR type or field name alone. Grades: **Enforced** (verified end to end), **Partial** (core
behavior present, remainder planned), **Application responsibility** (the framework provides the
mechanism; the host configures it).

| Spec | Area | Grade |
|---|---|---|
| RFC 6749 §3.1/§3.2 | Duplicate-parameter rejection at binding | Enforced |
| RFC 6749 §4.1.2/§10.5 | Authorization-code single use; replay cascades revocation of derived tokens | Enforced |
| RFC 6749 §5.2, OIDC Core §3.1.2.6 | Token-endpoint error-code families | Enforced |
| RFC 8252 §7.3/§8.3 | Loopback redirect URI port variance (`localhost` excluded by §8.3) | Enforced |
| RFC 9068 §2.1/§2.2, RFC 8707 §2 | Access token `typ: at+jwt`; `aud` = `DefaultResource ?? Issuer` when no resource parameter is sent | Enforced |
| OIDC Core §2 | ID token `aud` = `client_id` | Enforced |
| RFC 7517 §4.5 | JWKS publishes every valid and retired issuer signing row with its `kid`; multi-key sets require `kid` on each row | Enforced |
| RFC 9700 §4.16 | Rendered pages carry `X-Frame-Options: DENY` and `CSP: frame-ancestors 'self'` | Enforced |
| RFC 9700 §2.6, RFC 10017 §6.3.3.4 | CORS on public token endpoints | Application responsibility — `SchemataCorsFeature` wires app-level CORS; the host decides origins and reachable endpoints |
| Front-Channel Logout §2 | `iss` and `sid` appended to `frontchannel_logout_uri` as a pair | Enforced |
| Back-Channel Logout §2.4 | Logout token `typ: logout+jwt` | Enforced |
| RP-Initiated Logout §2 | OP session invalidation before RP notifications; failure closes the logout | Enforced |
| OIDC DCR §2-§3 | Dynamic registration: full metadata validation, 201 creation with paired registration access token + `registration_client_uri`, Bearer read-back; registration requests pass an initial access token gate satisfied by a host-supplied `IInitialAccessTokenValidator` (anonymous requests rejected with 401); software statements are unapproved unless the host supplies a trusting validator | Enforced (registration surface) |
| RFC 8707 §3 | `resource` at the authorize and token endpoints: §2 syntax validation (`invalid_target`), code-payload and refresh-subset grant consistency, access-token `aud` restriction, introspection echo | Enforced |
| OIDC Core §5.3/§5.5 | UserInfo endpoint over bearer with the `openid` scope; `acr_values` voluntary satisfiability per §5.5.1.1 — the satisfied requested class is stamped as `acr` (level walk, stronger performed authentication covers a weaker request), an unsatisfiable request keeps the performed level, discovery `acr_values_supported` | Enforced (endpoint, `acr` semantics) / Not implemented (`claims` request parameter, signed UserInfo responses) |
| RFC 9396 | `authorization_details` validated against registered type descriptors and the client's `authorization_details_types` subset (§10), persisted onto grants, echoed at code exchange, discovery `authorization_details_types_supported`; without the feature the parameter is ignored and reaches no grant | Enforced (behind `UseRichAuthorizationRequests()`) |
| RFC 7523 | Assertion client authentication (`client_secret_jwt`, `private_key_jwt`); §3.1 `jwt-bearer` grant anchored on the `JwtBearerTrustedIssuers` table — an assertion issuer without an entry is rejected, and an empty table leaves the grant unusable | Enforced |
| RFC 9449 | DPoP proof validation, key-bound tokens, server nonces, discovery metadata, `dpop_jkt` authorize binding | Enforced (behind `UseDemonstratingProofOfPossession()`) |

## Caveats

- `Issuer` validation runs in `PostConfigure`, so a missing value surfaces as an
  `InvalidOperationException` when the options are first resolved. Token issuance resolves the
  signing rows under the issuer and throws the same exception when no valid signing row exists,
  when that row carries no algorithm or loadable material, or when a multi-key set carries a
  blank key id.
- The bridge is opt-in. Without `.UseIdentity()` on the authorization builder, tokens carry only
  the base claims; user claims do not appear.
- The device flow requires `DeviceVerificationUri`.
- Pairwise subjects require `UsePairwiseSubjects()` and a `SchemataSubjectMapping` repository so
  `PairwiseSubjectTranslator<TApp>` can retain its canonical-subject-to-pairwise-subject mappings.
- Token cleanup needs `SchemataSchedulingFeature` and a registered token repository.
- DPoP proof replay markers and server-provided nonces live in `ICacheProvider`: proof markers
  under direct cache keys, server nonces through the cache-backed `nonce` token-store slot. A
  multi-instance deployment needs a distributed implementation (Redis, SQL) so the caches are
  shared across nodes.

## See also

- [OIDC Server cookbook](../cookbook/oidc-server.md) — seed a client and drive the code + PKCE flow
- [Identity](identity.md) — the user store the bridge reads
- [Authorization guide](../guides/authorization.md) — a minimal client-credentials smoke test
