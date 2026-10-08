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
| `Schemata.Authorization.Identity` | `Features/SchemataAuthorizationIdentityFeature.cs`, `IdentitySubjectProvider.cs`, `Advisors/AdviceClaimsSubject.cs`, the `UseIdentity<TUser>()` builder extension |

## Enabling the server

```csharp
using Schemata.Identity.Skeleton.Entities;

builder.UseSchemata(schema => {
    schema.UseIdentity();

    schema.UseSecurity();                   // security rows, secret verification, token stores

    schema.UseAuthorization(o => {
              o.Issuer = "https://auth.example.com";
          })
          .UseAuthorizationCodeFlow()
          .UseRefreshTokenFlow()
          .UseUserInfo()
          .UseIdentity<SchemataUser>();
});
```

`UseAuthorization` has two overloads: a default one over `SchemataApplication`,
`SchemataAuthorization`, `SchemataScope`, `SchemataToken`, and a generic one for custom subclasses.
Both take an optional `Action<SchemataAuthorizationOptions>`, store it, add
`SchemataAuthorizationFeature<...>`, and return a
`SchemataAuthorizationBuilder<TApp, TAuth, TScope>` for chaining. The feature maps discovery and
JWKS endpoints directly from the configured issuer.

The host also supplies resource names. Register a consumer-owned `IRepositoryAddAdvisor<TEntity>`
before `AdviceAddCanonicalName.DefaultOrder` (120,000,000), as shown in
[the mutation pipeline](repository/mutation-pipeline.md#consumer-owned-resource-names), or set an
explicit `Name` before creation. Cover applications, scopes, authorizations, tokens, security rows,
and subject mappings, including rows created internally during protocol handling. Canonical-name
resolution fails with `ValidationException` when a required `Name` remains blank; the framework has
no fallback name generator. Protocol identifiers and secrets remain separate from resource names.

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
60_000_000 = 460_000_000`) depends on `SchemataAuthenticationFeature` and
`SchemataTransportHttpFeature`. `ConfigureServices`:

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

`ConnectController` is routed at `~/Connect`. Which actions a deployment actually serves is
governed by feature activation: each flow feature activates the Connect actions its handlers
back, an action without an activated owner is removed from the MVC application model — an
unmatched route returning 404 with no dispatch attempted — and shared actions (`Token`,
`Interact`) stay while any owner is installed. Discovery metadata and the Profile authorization
policy derive from the same activation facts. An installed feature whose handler registration is
broken still fails at dispatch; route absence never masks that.

| Method                    | Route                 | Action                                                | Spec                                                          |
| ------------------------- | --------------------- | ----------------------------------------------------- | ------------------------------------------------------------- |
| `GET` / `POST`            | `/Connect/Authorize`  | `AuthorizeGet` / `AuthorizePost`                      | RFC 6749 §3.1, Authorization Endpoint                         |
| `POST`                    | `/Connect/Token`      | `Token`                                               | RFC 6749 §3.2, Token Endpoint                                 |
| `POST`                    | `/Connect/Device`     | `Device`                                              | RFC 8628 §3.1, Device Authorization Request                   |
| `GET` / `POST` / `DELETE` | `/Connect/Interact`   | `Interact` / `ApproveInteraction` / `DenyInteraction` | consent interaction                                           |
| `POST`                    | `/Connect/Introspect` | `Introspect`                                          | RFC 7662 §§2.1–2.2, Introspection Request and Response        |
| `POST`                    | `/Connect/Revoke`     | `Revoke`                                              | RFC 7009 §§2.1–2.2, Revocation Request and Response            |
| `GET` / `POST`            | `/Connect/Profile`    | `Profile` (bearer-authorized)                         | OpenID Connect Core 1.0 §5.3, UserInfo Endpoint               |
| `POST` | `/Connect/Register` | `Register` | OIDC Dynamic Client Registration 1.0 §3.1 |
| `GET` / `PUT` / `DELETE` | `/Connect/Register/{clientId}` | `RegisterRead` / `RegisterReplace` / `RegisterDelete` | RFC 7592 §2 |
| `GET` / `POST`            | `/Connect/EndSession` | `EndSessionGet` / `EndSessionPost`                    | OpenID Connect RP-Initiated Logout 1.0 §2, RP-Initiated Logout |

`IAuthorizationSignInService` owns transport-neutral protocol issuance. `ConnectController` renders
endpoint token responses as JSON and callback parameters through `ResponseModeService` as query,
fragment, or `form_post`. `IAuthorizationSignInHttpWriter` renders only direct compatibility-scheme
sign-ins. Endpoint handlers remain transport-neutral and do not access `HttpContext`.

The authorization feature maps discovery from the exact configured
`SchemataAuthorizationOptions.Issuer`. A root issuer serves OIDC metadata at
`/.well-known/openid-configuration`, RFC 8414 metadata at
`/.well-known/oauth-authorization-server`, and keys at `/.well-known/jwks`. For an issuer with a
path such as `https://auth.example.com/tenant`, OIDC and JWKS append their suffixes to the issuer
path (`/tenant/.well-known/...`), while RFC 8414 inserts its suffix before the path
(`/.well-known/oauth-authorization-server/tenant`). The alternative root or transformed paths are
not aliases.

Issuer configuration is accepted only as an absolute HTTPS URI with an authority and without
userinfo, query, fragment, or a non-root trailing slash. The value is preserved verbatim as the
metadata `issuer`, token `iss`, and prefix of advertised protocol endpoints. OIDC and OAuth routes
share one `DiscoveryHandler<TScope>` and `IDiscoveryAdvisor` pipeline. Advisors advertise installed
host-wide capabilities; they do not scan per-client policy. Optional arrays and feature fields are
omitted when their source set or feature is absent, while OIDC-required arrays remain present.
`jwks_uri` points at the issuer-derived JWKS route.

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
| `UseAuthorizationCodeFlow()`              | `authorization_code` (+ PKCE), `/Connect/Authorize`               | `AuthorizationCodeFlowFeature` (+ `TokenFeature`, `InteractionFeature`) |
| `UseClientCredentialsFlow()` | `client_credentials`                                              | `ClientCredentialsFlowFeature`                                          |
| `UseRefreshTokenFlow()`      | `refresh_token`                                                   | `RefreshTokenFlowFeature`                                               |
| `UseDeviceFlow()`            | `urn:ietf:params:oauth:grant-type:device_code`, `/Connect/Device` (RFC 8628 §§3.1, 3.4) | `DeviceFlowFeature` (+ `InteractionFeature`)                            |
| `UseTokenExchange()`         | `urn:ietf:params:oauth:grant-type:token-exchange` (RFC 8693 §2.1)                       | `TokenExchangeFeature`                                                  |
| `UseJwtBearerGrant()`        | `urn:ietf:params:oauth:grant-type:jwt-bearer` (RFC 7523 §3.1; needs a trusted issuer)   | `JwtBearerGrantFeature<TApp>`                                           |
| `UseRichAuthorizationRequests()` | `authorization_details` at `/Connect/Authorize` (RFC 9396 §6; ignored when the feature is absent) | `RichAuthorizationRequestsFeature<TApp>`                              |
| `UseResourceIndicators()`    | `resource` at `/Connect/Authorize` and `/Connect/Token` (RFC 8707 §§2-2.2; ignored when the feature is absent) | `ResourceIndicatorsFeature<TApp>`                                       |
| `UseClaimsParameter()`       | `claims` at `/Connect/Authorize` (OIDC Core §5.5; ignored when the feature is absent) | `ClaimsParameterFeature<TApp>`                                                  |
| `UseClientAssertionAuthentication()` | `client_secret_jwt` / `private_key_jwt` client authentication channels (RFC 7523 §2) | `ClientAssertionAuthenticationFeature<TApp>`                          |
| `UseIntrospection()`         | `/Connect/Introspect` (RFC 7662 §§2.1–2.2)                                             | `IntrospectionFeature`                                                  |
| `UseRevocation()`            | `/Connect/Revoke` (RFC 7009 §§2.1–2.2)                                                 | `RevocationFeature`                                                     |
| `UseDynamicClientRegistration()` | `/Connect/Register` (OIDC DCR 1.0 §§3.1-3.3; create gated by a host-supplied `IInitialAccessTokenValidator` with anonymous requests rejected with 401; RFC 7592 §2.2 replace is gated by the registration access token and clears omitted writable fields instead of merging) | `DynamicRegistrationFeature`                                            |
| `UseUserInfo()`              | `/Connect/Profile` (OpenID Connect Core 1.0 §5.3)                                     | `UserInfoFeature`                                                       |
| `UseEndSession()`            | `/Connect/EndSession` (OpenID Connect RP-Initiated Logout 1.0 §2)                     | `EndSessionFeature`                                                     |
| `UseFrontChannelLogout()`    | front-channel logout metadata                                     | `FrontChannelLogoutFeature`                                             |
| `UseBackChannelLogout()`     | back-channel logout queue + notifier                              | `BackChannelLogoutFeature`                                              |
| `UsePairwiseSubjects()`      | pairwise `sub` projection + discovery advertisement (OIDC Core 1.0 §8)  | `PairwiseSubjectsFeature<TApp>`                                                 |

`UseClaimsParameter()` owns parameter parsing and final Essential ACR approval checks. Hosts without
that capability ignore the parameter; the mandatory OAuth/OpenID profile selector does not activate it.
PAR failures return direct JSON errors even when shared authorization advisors carry browser callback
metadata. Location-bound introspection details are restricted to the intersection of the token's
verified audiences and the authenticated caller's configured resource audiences.

`UseAuthorizationCodeFlow` and `UseRefreshTokenFlow` accept optional `Action<CodeFlowOptions>` /
`Action<RefreshTokenFlowOptions>` configurators. `TokenFeature` is shared: any grant that lands on
`/Connect/Token` pulls it in.

`POST /Connect/Token` dispatches by `grant_type` to the registered `IGrantHandler`. Before the
grant runs, the `ITokenRequestAdvisor<TApp>` chain validates the request:

| Advisor                                 | Checks                                                      |
| --------------------------------------- | ----------------------------------------------------------- |
| `AdviceRequestEndpointPermission<TApp>` | The client holds the `e:/Connect/Token` permission          |
| `AdviceRequestGrantPermission<TApp>`    | The client's registered `grant_types` list the requested grant type |
| `AdviceRequestScopeValidation<TApp>`    | Requested scopes are within the client's registered `scope`         |

### Token exchange profiles

`UseTokenExchange()` routes RFC 8693 exchanges to keyed `ITokenExchangeHandler<TApp>`
registrations. The router resolves the exact `subject_token_type|requested_token_type` composite
key first. The subject-only key applies when `requested_token_type` is absent or one of the
standard RFC 8693 §3 types. A custom `requested_token_type` requires its exact composite
registration; without one the exchange fails with `invalid_request` naming
`requested_token_type`. When the requested type is absent or standard and no eligible profile
exists, the error names `subject_token_type`. Hosts add profiles by registering their own keyed handlers, and each
handler validates its subject, actor, audience, resource, and scope inputs before producing the
response. Installing the feature advertises the grant type in the discovery document.

Repeated `audience` and `resource` parameters bind to collections and reach the selected profile.
Native SSO requires exactly one audience equal to the issuer; other profiles own their target rules.

### Native SSO issuance

`UseNativeSingleSignOn()` routes the ID-token/device-secret exchange through
`NativeSsoTokenExchangeHandler` and the shared `AuthorizationSignInService`. The issuer applies
the configured access-token format, claims advisors, destinations, and target-client subject
projection. Reference access tokens persist a signed payload for authentication and introspection.

The handler requires trusted device-secret provenance and live online-session or offline-family authority.
Target scopes must fit registration policy and a valid target consent record, unless the configured
`IConsentModelProvider` grants implicit consent. Registered source scopes are not consent evidence;
independent target authorization may grant additional scopes. Omitted scope uses the persisted source
grant, checked against the same target policy. OAuth-only descendants cannot regain OIDC eligibility.

During refresh, `AdviceRefreshTokenDeviceSecret` searches by session and checks the secret's
application, session, resolved device, status, and expiry. Existing records with a null `Parent`
remain eligible. `TokenHandler` carries the selected secret and session in sign-in properties so
the response issuer can return the secret together with an ID token containing `ds_hash` and `sid`.
Native token exchange follows the shared ID/refresh-token grant and scope rules; it does not
rotate or echo the device secret.

### Request Object lifetime

`RequestObjectReader` validates optional `exp` and `nbf` NumericDate claims before merging request
parameters. The check applies to signed objects and to unsigned objects permitted by the signing
policy. A one-minute clock tolerance applies: `exp <= now - 60 seconds`,
`nbf > now + 60 seconds`, or `nbf > exp` produces `invalid_request_object`. Present values must
be JSON numbers within the supported date range; malformed values are rejected rather than treated
as absent. Fractional seconds are retained for comparison.

PAR restores the original Request Object and runs the reader again during authorization, so a
still-valid PAR handle does not extend the JWT's lifetime. These checks implement the optional
claim semantics in [RFC 7519 §§4.1.4–4.1.5](https://www.rfc-editor.org/rfc/rfc7519.html#section-4.1.4);
they do not require `exp`, `nbf`, or `jti` to be present.

### Token timestamps and refresh bounds

Issuance samples the configured `TimeProvider` once before key resolution or signing. The selected
absolute issue and expiry timestamps are passed through token creation and row persistence, so a
JWT `iat`/`exp` pair and its `SchemataToken.ExpireTime` share the same temporal authority.

The first refresh token stores its absolute chain deadline in the trusted grant context. Each
rotation applies the earlier of that deadline and `issuedAt + RefreshTokenLifetime`; key-loading or
signing latency cannot move the successor beyond either bound. A refresh request at or after the
deadline fails before redemption or successor issuance. The framework does not apply a sliding or
inactivity extension to this deadline.

### Refresh family fencing

Rotation-enabled refresh grants carry a semantic family identifier separate from resource `Name`,
authorization, session, and device identifiers. The repository token store keeps an active family
marker and commits predecessor redemption, successor rows, and marker-version rotation through one
unit of work. A concurrent rotation therefore publishes one successor set.

Refresh-token revocation and authenticated reuse invalidate the family marker and revoke its rows;
unrelated families and sessions remain valid. Access-token-only revocation affects that token alone.
Authorization-code replay, including a redemption loser, completes family invalidation and grant-wide
token revocation before `invalid_grant`. Storage failures propagate. Each repository-store operation
resolves a fresh repository; one scoped store remains usable after success, rollback, or a lost CAS.
A rotation finishing after invalidation cannot commit. Missing family state is rejected; unchanged
storage after a failed optimistic write surfaces as a configuration error. Pruning retains markers.
Native SSO device secrets share the refresh family and its fenced token batch.

`RepositoryTokenStore.TryRedeemAsync` checks cancellation first, then admits only a supplied Valid token.
Fresh Redeemed or Revoked decisions return false without a storage write or stamp change. A stale Valid
instance still uses native timestamp compare-and-swap and loses to a concurrently consumed token.

### Registration access token rotation

RFC 7592 management rotation is fenced through `ITokenStore<TToken>.TryRotateAsync`, not through
a refresh-family batch. The store redeems the predecessor and publishes the prepared successors
in one repository unit of work; a rotation that loses the concurrency check returns `false` and
publishes no successor. The framework never returns a redeemed predecessor as usable: GET and PUT
respond `401 invalid_token` once the row has moved.

`RegistrationTokenLifetime` keeps its two existing forms. A null value stays non-expiring on the
initial token and on every successor the rotation publishes. A finite value supplied by the
handler row factory stamps the same expiry on each successor; the store neither invents nor caps
the lifetime.

Software statements use the host's `ISoftwareStatementValidator`. Return
`SoftwareStatementValidationResult.Invalid` for failed signature or validity checks,
`Unapproved` for a valid statement the server does not approve, or `Approved(claims)` for verified,
approved metadata. The first two map to `invalid_software_statement` and
`unapproved_software_statement`. Approved claims override the plain request before mapping and
advisors on both POST and PUT. Server-managed identifiers and credentials are excluded from that
overlay; malformed metadata types produce `invalid_client_metadata`. Without a host validator,
the server rejects statements as unapproved.

`UseDynamicClientRegistration()` installs the create (POST) and read (authorized GET) capabilities.
Replace and delete are separate installations: `UseRegistrationReplace()` adds PUT and
`UseRegistrationDelete()` adds DELETE at the configuration URI. An operation that was never
installed has no handler and no route — its HTTP verb is rejected (405 where a sibling verb shares
the route) and local dispatch finds no handler. Omitting the registration feature removes its
routes entirely (404).
Language-tagged client names and URI metadata retain their tags through storage and readback;
enabled DPoP/RAR registration advisors own their metadata, and PUT clears omitted values.

### Application lifecycle transactions

Application manager deletion physically deletes its canonical-reference security materials,
authorization grants, pairwise mappings and tokens, including session participants and inactive rows.
A configured application implementing `ISoftDelete` receives the same cleanup when its manager
persists a non-null `DeleteTime`. Shared issuer keys, users, OP sessions, refresh-family markers and other applications survive.
The base application has no disable state; host-defined disable policies remain host-owned.

`ApplicationResourceMutation<TApplication,TAuthorization>` owns the transaction and joins its
dependent repositories; the application manager delegates every write to this owner through
`IResourceMutation<TApp>`. Compatible provider/context registrations are required. Failure disposes
the uncommitted transaction and rolls back its changes. Direct repository writes bypass this policy.

Native HTTP/gRPC application CRUD resolves the same `IResourceMutation<TApp>` owner after resource
authorization, freshness checks and field-mask mapping. The owner commits once. Normal soft
deletion performs cleanup before later expunge/purge operations.

Publication callers pass an explicit store `beforeCommit` participant invoking the manager's
`EnlistPublicationAsync` or `EnlistTokenPublicationAsync`. The participant joins the actual store
transaction and updates the current application's concurrency stamp. This changes Application
ETags and can abort concurrent issuance or management writes. Token replay classification handles
only provider-identified token conflicts; parent conflicts propagate unchanged. Revocation and deletion
carry no publication fence. Custom stores execute participants inside their atomic transaction; separate
databases need host-owned coordination. Store calls without a participant remain low-level APIs.

Canonical names remain reusable after reconstruction; relationships contain no incarnation UID.

Implementation: `src/Schemata.Authorization.Foundation/Managers/SchemataApplicationManager.cs`.

### Native SSO session authority

Native SSO records whether the grant uses online or offline session authority. An online grant has
a server-side OP-session authority slot with its own configured lifetime. The slot carries an
opaque generation minted at establishment, and the grant persists the generation it was born
under; Native exchange and Native refresh must prove that exact generation against a live slot.
Expiry replaces the slot with a fresh generation and browser sign-out removes it, so a later
authorization under the same subject and session identifier establishes a new generation and
grants born under an older one return `invalid_grant`. Remaining access or ID token rows do not
prove the online session is active.

An `offline_access` Native grant uses its refresh-family marker as the offline authority. Refresh
rotation preserves that marker; family invalidation, original offline refresh revocation, or expiry
makes Native exchange and related refresh use invalid. Browser logout clears online authority but
does not end a separate offline family. Browserless continuations use the authority persisted on the
grant and never acquire an ambient browser session identifier.

### OAuth and OpenID Connect grant profiles

The authorize pipeline selects a trusted profile after PAR and Request Object normalization and
after client scope authorization. An authorized `openid` scope selects OpenID Connect; other
authorized scopes select OAuth. The profile is stored with the grant context and inherited by
authorization-code, refresh, device, and Native SSO continuations.

OAuth grants retain authorization code, PKCE, state, access and refresh tokens, DPoP, PAR, Request
Objects, rich authorization details, resource indicators, interaction parameters, authentication
evidence, and the configured `offline_access` convention. The Profile endpoint also accepts these
grants when the persisted subject category is an end user. Application grants, including
`client_credentials`, cannot use Profile as an end-user identity source.

ID Tokens, `session_state`, relying-party logout participation, and Native SSO device secrets are
created only for an OpenID Connect grant. An OAuth authorization request that supplies `id_token`,
top-level `nonce`, `claims.id_token`, `id_token_hint`, or `device_sso` is rejected as an incompatible
profile request. DPoP nonces are independent proof values carried in DPoP protocol messages.

`session_state` follows the OpenID Connect Session Management contract with a salt-only
interaction carrier. The authorize advisor mints a random salt per request, overwrites the
server-side `AuthorizeRequest.SessionStateSalt` with it, and carries it through the interaction
payload into the sign-in properties, so a client-supplied value never survives. The initial
value binds SHA-256 over `client_id`, the redirect origin, the current OP user-agent state
cookie value, and the salt, and serves same-request error redirects. The successful callback
recomputes it after session issuance from the live OP user-agent state; a sign-in that rotated
that state publishes the post-login value, while only the salt ever crosses the interaction.

Scope narrowing creates a descendant grant with its own ceiling. Dropping `openid` converts that
descendant to OAuth, and a later refresh cannot restore either the scope or OpenID Connect profile.
The separately issued parent credential remains unchanged. Discovery continues to describe the
features installed on the server rather than one grant's profile.

## Advisor families

Advisor chains use `TryAddEnumerable`. Claims and destination advisors receive `AuthorizationClaimContext`
for grant evidence, DPoP, requested claims, and handled output; refresh publication keeps its predecessor local.

| Interface                                                                                                                                                                        | Generic params     | Role                                                        | Built-ins                                                                                                                                                                                                                                                                                                        |
| -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------ | ----------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `IDiscoveryAdvisor`                                                                                                                                                              | —                  | Populate the discovery document                             | `AdviceDiscoveryBase`, `AdviceDiscoveryClientAuthentication`, and `AdviceDiscoveryAcrValues` plus one per flow (`AdviceDiscoveryCodeFlow`, `AdviceDiscoveryRefreshToken`, `AdviceDiscoveryDeviceFlow`, `AdviceDiscoveryIntrospection`, `AdviceDiscoveryRevocation`, `AdviceDiscoveryUserInfo`, `AdviceDiscoveryEndSession`, …) |
| `IClaimsAdvisor`                                                                                                                                                                 | —                  | Enrich the principal before token issuance                  | `AdviceClaimsAudience`, `AdviceClaimsAuthenticationContext`, `AdviceClaimsPairwise<TApp>` (pairwise flow feature), and `AdviceClaimsSubject` (Identity bridge)                                                                                                                                                         |
| `IDestinationAdvisor`                                                                                                                                                            | —                  | Route each claim to access token, ID token, and/or UserInfo | `AdviceDestinationSubject`, `Advice{Profile,Email,Phone,Address,Role}ClaimDestination`                                                                                                                                                                                                                           |
| `ITokenRequestAdvisor<TApp>`                                                                                                                                                     | `TApp`             | Validate the token request                                  | `AdviceRequestEndpointPermission`, `AdviceRequestGrantPermission`, `AdviceRequestScopeValidation`                                                                                                                                                                                                                |
| `IAuthorizeAdvisor<TApp>`                                                                                                                                                        | `TApp`             | Validate the authorize request                              | `AdviceAuthorizeClientAndRedirect`, `AdviceAuthorizeEndpointPermission`, `AdviceAuthorizeGrantPermission`, `AdviceAuthorizeGrantProfile`, `AdviceAuthorizePkce`, `AdviceAuthorizeNonce`, `AdviceAuthorizePrompt`, `AdviceAuthorizeResponseMode`, `AdviceAuthorizeConsent`, `AdviceAuthorizeAutoApproveSignIn` |
| `ICodeExchangeAdvisor` / `IRefreshTokenAdvisor` / `IIntrospectionAdvisor` / `IRevocationAdvisor` / `IUserInfoAdvisor` / `IDeviceAuthorizeAdvisor` / `IDeviceCodeExchangeAdvisor` | `TApp` | Validate each endpoint's request                            | `AdviceCodeExchange*`, `AdviceRefreshTokenValidation`, `AdviceIntrospection*`, `AdviceRevocation*`, `AdviceUserInfoEndUserRequirement`, `AdviceDevice*`                                                                                                                                                           |
Authorize/PAR advisors share explicit `AuthorizeContext` stage and continuation data; executable flow features own endpoint `Actions`.

## Permissions

A client's endpoint capabilities are a list of permission strings on `SchemataApplication.Permissions`,
prefixed per `AuthorizationConstants.PermissionPrefixes`:

| Prefix | Constant   | Example                                    |
| ------ | ---------- | ------------------------------------------ |
| `e:`   | `Endpoint` | `e:/Connect/Token`, `e:/Connect/Authorize` |

`IApplicationManager<TApp>.HasPermissionAsync(app, permission, ct)` is the lookup the endpoint
permission advisors use. Grant-type, response-type, and scope admission read the typed `GrantTypes`,
`ResponseTypes`, and `Scope` fields — the same metadata dynamic registration writes and the
management resources expose — through `HasGrantTypeAsync`, `HasResponseTypeAsync` (component
order-insensitive), and `HasScopeAsync`. `Permissions` contains administrator-assigned endpoint
policy. PKCE policy comes from `CodeFlowOptions`; consent policy is resolved per request through
`IConsentModelProvider`, whose Foundation default is `ExplicitConsentModelProvider`.

## Audience and application bindings

`SchemataApplication.Name` is the consumer-owned resource identifier, independent of the OAuth
`ClientId`. Its canonical name follows `applications/{application}` using `Name`, so a client with
`ClientId = "test-client"` and `Name = "client-resource"` is referenced as
`applications/client-resource`. Client lookup continues to use `ClientId`.

Dynamic registration assigns a protocol `ClientId`, then creates the application through its
repository pipeline. It uses the created application's `CanonicalName` for credential parents and
registration-token bindings after the consumer naming advisor has run. It does not derive those
references by interpolating `ClientId`.

The implementations are `src/Schemata.Authorization.Skeleton/Entities/SchemataApplication.cs`,
`src/Schemata.Authorization.Foundation/Managers/SchemataApplicationManager.cs`, and
`src/Schemata.Authorization.Foundation/Handlers/RegisterHandler.cs`.

`AdviceClaimsAudience` preserves an explicit `aud` claim set. Otherwise, it mints two claims, each
pre-tagged with a single destination so the
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

Default Application, Scope and Authorization managers resolve a fresh read repository from their
actual scoped service provider for each operation. Lazy lists keep that repository alive until
enumeration ends. A detached update followed by another read through the same manager observes
committed metadata and its current concurrency stamp. Identity User and Role stores use the same
operation-local primary-entity read boundary.


Client credentials and assertion keys live in security rows addressed through `SecurityParents`
(`Application(app)` returns the created application's `CanonicalName`; `Issuer(issuer)` returns the
issuer URI) and read through `ISecurityStore<TSecurity>`, which `UseSecurity()` registers. The shared
`ClientSecretValidator` verifies the presented secret against the client's newest valid
`password` row (`usage=authentication`) with `ISecretVerifier`; `client_secret_jwt` reads
`secret` rows; `private_key_jwt` loads JOSE material from `jwk`, `jwks`, and `jwks-uri` rows
through `ToKeyMaterialAsync`, adapted by `SecurityKeyAdapter`.

Dynamic registration provisions a usable symmetric `secret` row for `client_secret_jwt` and
returns that credential to the client. Its registered `token_endpoint_auth_signing_alg` restricts
accepted assertions; password-authentication rows remain hashed. ID-token issuance defaults
`id_token_signed_response_alg` to RS256, independently of the issuer's primary signing algorithm.
Registration replacement rejects transitions into `client_secret_jwt`, or from that method to
Basic/Post, before updating metadata. Provision a new client when changing between raw HMAC material
and password-hash credentials; the existing secret remains usable after a rejected replacement.

Protected UserInfo uses the client's registered signing algorithm and current recipient JWKS
material. Recipient keys must allow encryption and the selected algorithm; revoked key rows are
excluded. An encryption `alg` without `enc` uses A128CBC-HS256. Boolean, numeric, array and object
claims retain their JSON types in plain and protected responses.

`ClientAuthenticationService` admits a grant on the mechanism the request actually presented — a
Basic header, a POSTed `client_secret`, a jwt-bearer `client_assertion`, or identification by
`client_id` alone — and runs only that method's authenticator; two genuinely presented credentials
are rejected, `invalid_request` for the generic RFC 6749 §3.2.1 case and assertion-specific
`invalid_client` when a client assertion is among them (RFC 7521 §4.2.1). A `none` identification locates the public client but is not proof of a
credential: the `client_credentials` grant rejects it because RFC 6749 §4.4.2 requires an
authenticated confidential client.

The device authorization, introspection, and revocation endpoints bind `client_assertion` and
`client_assertion_type` with `client_id`/`client_secret` and forward all four fields to
`ClientAuthenticationService`, so registered `client_secret_jwt` and `private_key_jwt` clients
authenticate at these endpoints under the same registered-method and signing-algorithm selection
as at the token endpoint. The assertion channel accepts the issuer and the token endpoint URL as
audiences at every endpoint (RFC 7523 §3), and the pushed authorization request endpoint already
accepts its own URL (RFC 9126 §2). In the same way, each of these three endpoints publishes its
own URL (`{issuer}/Connect/Device`, `{issuer}/Connect/Introspect`, or `{issuer}/Connect/Revoke`)
as the explicit `endpointAudience` argument to client authentication. Each invocation retains its
own audience set; the assertion channel reads no ambient endpoint state.

Rows persist verbatim, in plaintext at rest.

### Persisted authentication events

`AuthorizationSignInService` resolves the authentication event after session or continuation
identity is final. A new authentication calls `IAuthenticationContextProvider` once; an
authorization-code, device-code, refresh, or validated Native SSO continuation inherits the
persisted `AuthorizationGrantContext` instead. The context records the canonical subject and
subject category, effective scope/profile, grant source, optional session association, and the
original optional `acr`/`amr`/`auth_time` evidence. Missing evidence stays absent; renewal does not
synthesize `auth_time` from the issuance clock.

The same context is serialized onto authorization-code, access-token, refresh-token, and Native
SSO device-secret rows. Claims advisors project that context onto access/ID token destinations;
they do not call the provider on a continuation. Introspection reads `acr` and `auth_time` from the
stored row context, so the persisted lineage remains authoritative even when a JWT claim view
would differ. `Claim.Properties` remains an in-process destination mechanism and is not persisted
as protocol context.

`SchemataToken.Key` holds a semantic slot identifier. Repository slot operations address
`(Parent, Provider, Key)`, backed by that composite unique index. `Name` has its own unique index and
identifies the token resource; `FindByNameAsync` still queries that resource name. Cache-backed slot
results carry `Key` and do not acquire a resource `Name` or pass through a repository naming advisor.
Persisting a slot through the repository-backed store requires the consumer naming policy.

`SchemataSecurity.Key` holds a parent-scoped credential label, distinct from its resource `Name`.
The key separation does not introduce a credential-selection API or policy: `ISecurityStore`
continues to list by parent with optional kind, usage, and status filters. The repository store
orders those rows by descending creation time, then `Key`. Protocol consumers keep their existing
credential-kind and status selection rules. See [Security](security.md#stored-resource-identity).

The source paths are `src/Schemata.Security.Skeleton/Entities/SchemataToken.cs`,
`src/Schemata.Security.Skeleton/Entities/SchemataSecurity.cs`,
`src/Schemata.Security.Foundation/Stores/{RepositoryTokenStore,CacheTokenStore,SecurityStore}.cs`,
and `src/Schemata.Authorization.Foundation/Services/SecurityParents.cs`.

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
`BackChannelLogoutJob`. The service snapshots the recipient facts (notification URI, audience,
subject, session, and the client's registered `id_token_signed_response_alg`) into the job
variables, one job per relying party. At execution time the job builds the logout token and signs
it through `TokenService.BeginSigningAsync`, so the registered ID-token algorithm becomes the
logout token's JWS `alg` when the issuer holds a valid signing row serving it; an algorithm with no
serving row throws. The token gets a two-minute lifetime measured from the execution clock and is POSTed
form-urlencoded as `logout_token`.
Missing facts, signing failure, a non-success response, or a non-cancellation transport failure
throws and Scheduling records the execution as failed. Cancellation, including HTTP timeout,
leaves the execution running for re-dispatch. The job has no cron schedule.

## Identity bridge

`Schemata.Authorization.Identity` binds `.UseIdentity<TUser>()` on `IAuthorizationBuilder` to the
host's existing Identity installation. The generic authorization builder implements that receiver;
call the bridge after entity-specific flow methods or retain the original builder for further setup.
`TUser` derives from `SchemataUser`. Registration selects `IdentitySubjectProvider<TUser>` explicitly,
independently of validator descriptors and registration order.

`SchemataAuthorizationIdentityFeature<TUser>` runs at priority 460,100,000 after the Authorization
and Identity features. It registers the subject provider, `AdviceClaimsSubject`, and owned-session
integration. The provider projects `sub`, `preferred_username`, `email` (+`email_verified`),
`phone_number` (+`phone_number_verified`), `nickname`, and `role` claims from the selected user.

The bridge registers `OpSessionIdentityObserver` as an `IHostSignInObserver`. At host sign-in it
stamps the canonical session identifier on the ticket under the configured `SessionIdClaimType`.
At ordinary Identity sign-out it resolves the subject and session from the host principal and runs
the same `IOpLogoutService` orchestrator before the controller performs its final scheme sign-outs.
The bridge may clear its owned application ticket while invalidating the session. When the
orchestrator prepared front-channel logout URIs, the observer returns the rendered logout page and
the host answers the sign-out request with that page after the final sign-outs; otherwise sign-out
returns no content. At most one observer may supply a response, and conflicting responses fail the
sign-out. An observer failure stops the controller's remaining final sign-outs, while observer-owned
ticket clearing and completed effects such as session invalidation or dispatched notifications are
not rolled back. The observer contract lives in
`Schemata.Identity.Skeleton`, so Identity carries no Authorization dependency; without the bridge a
host signs out standalone.

## Entity types

All entities use `Guid Uid` as the primary key and carry `[PrimaryKey(nameof(Uid))]`:

| Entity                  | Table                    | Canonical name                   | Notable properties                                                                                                                                      |
| ----------------------- | ------------------------ | -------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `SchemataApplication`   | `SchemataApplications`   | `applications/{application}`     | `Name` (resource identifier), `ClientId` (independent protocol identifier), `GrantTypes`, `ResponseTypes`, `Scope`, `TokenEndpointAuthMethod`, `IdTokenSignedResponseAlg`, `RedirectUris`, `PostLogoutRedirectUris`, `Permissions`, `BackChannelLogoutUri`, `BackChannelLogoutSessionRequired`, `RequirePushedAuthorizationRequests`, `RequestObjectSigningAlg`, `RequireSignedRequestObject`, `AuthorizationDetailsTypes`, `DpopBoundAccessTokens` |
| `SchemataAuthorization` | `SchemataAuthorizations` | `authorizations/{authorization}` | `Application` (canonical reference), `Subject`, `Type`, `Status`, `Scopes`, `CodeChallengeMethod`                                                        |
| `SchemataScope`         | `SchemataScopes`         | `scopes/{scope}`                 | `Name`, `Resources`                                                                                                                                     |
| `SchemataToken`         | `SchemataTokens`         | `tokens/{token}`                 | `Name` (resource identifier), `Key` (semantic slot identifier), `Parent` (subject reference), `Application` and `Authorization` (canonical references), `Provider`, `SessionId`, `Type`, `Status`, `Format`, `ReferenceId`, `Payload`, `Value` (slot payload), `ExpireTime`; defined in `Schemata.Security.Skeleton` |
| `SchemataSubjectMapping` | `SchemataSubjectMappings` | `subjectMappings/{subjectMapping}` | `Application` (canonical reference), `CanonicalSubject`, `PairwiseSubject`, `SectorHost` |
| `SchemataSecurity`      | `SchemataSecurities`     | `securities/{security}`          | `Name` (resource identifier), `Key` (credential label), `Parent` (host-resource canonical name or issuer URI), `Kind`, `Algorithm`, `Usage`, `Kid`, `Value` (plaintext material), `Status`; defined in `Schemata.Security.Skeleton` |

### Required schema and data migration

Existing deployments must migrate storage and host registration before adopting the independent
resource-name fields. This is a breaking storage and naming contract, not an automatic upgrade:

- Add and populate `SchemataToken.Key` from the old `Name` values used as semantic slot identifiers.
  Move the slot uniqueness constraint to `(Parent, Provider, Key)` and populate independent resource
  names satisfying the unique `Name` index. OAuth token resource names are not slot keys.
- Add and populate `SchemataSecurity.Key` from old `Name` values used as credential labels, and move
  the parent-label index to `(Parent, Key)`. Supply independent resource names for security rows.
- Populate the independent `SchemataApplication.Name` field while retaining every existing
  `ClientId`. A host may deliberately retain the previous canonical path by using its existing leaf
  as `Name`; this is a migration decision, not an alias or framework fallback.
- Preserve canonical references when retaining names. When names change, migrate affected
  `CanonicalName` values and all referring data together, including credential `Parent`, token and
  authorization `Application`, subject-mapping `Application`, and token `Authorization` references.
  Migrate host-owned references as well; changing the application name must not change OAuth
  `client_id` values.
- Register naming advisors for all resources created by enabled flows, including internally created
  authorizations, tokens, security rows, and pairwise subject mappings. Application seed code may
  instead supply explicit names.

### Canonical logout target and approval

`EndSessionHandler` validates signature, issuer and client, translates pairwise subjects, then calls
`IOpSessionService.ResolveLogoutAsync`. Installed `IOpSessionStore.ReadLogoutAsync` adapters run in
`Order` and combine proof for one `LogoutSessionTarget` whose `Application` is the canonical RP.
`MatchesCurrent` and `HasRecentEvidence` describe operation proof: a live RP credential plus host
or online authority. Application adapters supply recent proof; participants identify notification recipients.

Expired hints may select a proved current target; other hints require confirmation. Cookieless hints
retain validated RP authority. Different-user requests discard that RP redirect authority. Approval
matches canonical subject and authority-resolved SID, including a SID-less ticket's mirror. A captured
null SID is valid for an unchanged subject-scoped host ticket only while approval still resolves null;
a newly identified session or mirror fails with `invalid_request`. Missing interaction returns
`server_error` before effects. Registered redirects
match exactly; state follows completed logout. See [§2](https://openid.net/specs/openid-connect-rpinitiated-1_0.html#RPLogout)
and [§3](https://openid.net/specs/openid-connect-rpinitiated-1_0.html#RedirectionAfterLogout).

`IOpLogoutService` is the single logout owner, shared by the RP-Initiated Logout endpoint and the
Identity sign-out bridge. The orchestrator is protocol-independent: it consumes a principal plus
subject/session identifiers and returns the front-channel URIs for the endpoint or host bridge to
render. When the caller supplies only a subject, it first resolves one canonical session identifier
from the host/browser adapters, then uses that same target for recipient preparation, invalidation,
notification dispatch, and participation retirement. It prepares every registered notifier's
relying-party recipients and messages, invalidates the canonical OP session (Identity, browser,
online-authority, and custom adapters run in order and aggregate independent failures after every
applicable cleanup is attempted), dispatches the notifications, and retires the targeted
participation facts. A failed invalidation closes the logout with `server_error` before dispatch or
retirement run; a failed dispatch aborts retirement so the participation rows survive for a later
retry.

Relying-party participation is an explicit token-store fact recorded once per subject, session,
and application when an OIDC grant publishes a relying-party artifact; OAuth grants never
participate. Notifiers read these facts, so credential expiry, pruning, and revocation never lose
a logout recipient. Offline family markers, offline refresh tokens, and device secrets are not
logout targets and remain active afterward.

Schemata supplies neither an automatic data migration nor compatibility aliases or fallback naming
paths. Adapt the schema migration to the selected persistence provider and the deployed data.

## SchemataAuthorizationOptions

Signing and encryption key material is served from security rows under the issuer, and client
secrets live in rows under each application (see Managers). Lifetimes and formats have defaults:

| Property                                    | Default          | Notes                                                       |
| ------------------------------------------- | ---------------- | ----------------------------------------------------------- |
| `Issuer`                                    | —                | Required (`iss` claim, discovery base URL)                  |
| Issuer signing rows                         | —                | `SchemataSecurity` rows under `SecurityParents.Issuer(Issuer)` with `usage=signing`; the newest `valid` row signs, `valid` and `retired` rows verify |
| Issuer encryption rows                      | none             | `usage=encryption` rows; the newest `valid` row encrypts JWE output; `valid` and `retired` keys decrypt retained ciphertext, while revoked keys are excluded |
| `ResourceAudiences` | `null` | Audiences this server accepts for itself when validating presented access tokens as a resource server (RFC 9068 §4); defaults to the non-blank `DefaultResource` and `Issuer` |
| `IntrospectionResourceAudiences` | empty | Maps an introspection caller's `client_id` to the resource identifiers it represents (RFC 7662 §2/§2.1). The policy applies to access tokens only: an access token reports active when a verified `aud` value matches one of the caller's entries (ordinal); a missing or empty entry still authorizes the endpoint but reports every access token inactive. Other token types remain governed by their validity rules. |
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
| `TokenValidationClockSkew` | 1 minute | Ordinary token and client-assertion timestamp tolerance using the injected `TimeProvider`; DPoP and request-object freshness remain separate. |
| `AcrValuesSupported`                        | empty            | Authentication Context Classes the deployment supports; advertised as the discovery `acr_values_supported` array and omitted while empty |
| `JwtBearerTrustedIssuers`                  | empty            | `jwt-bearer` grant trust anchors (RFC 7523); register each external issuer's public key through `AddJwtBearerTrustedIssuer(issuer, key)` |

Authorization configuration runs through the DI options lifecycle once per options instance.
Authentication scheme registration, the Profile policy, and runtime consumers use the final
`IOptions<SchemataAuthorizationOptions>` value, including `PostConfigure` scheme overrides.

Connect endpoint routes follow the configured issuer path as well as their advertised URLs.
Discovery intersects configured client-authentication methods with installed authenticators and
imports active issuer signing keys before advertising their algorithms. Broken active key material
fails discovery; retired keys remain verification material rather than advertised issuance capability.

Native private-use redirect schemes require a period by default (`RequireNativeSchemeDomain`);
set it to `false` only for a deliberate historical registration profile. Claimed HTTPS and loopback
IP redirects retain their existing rules. Native shared secrets identify clients without proving
confidential identity. `AllowHttpLogoutUris` permits HTTP logout targets only for confidential
clients; front-channel targets must also share scheme, host, and port with a registered redirect.
Native post-logout callbacks may use alternate schemes. Public registration JWKS reject private
and symmetric key material on write and readback.

`PermitResponseType(...)` and `AddJwtBearerTrustedIssuer(...)` are fluent helpers on the options object. DPoP proof configuration lives on `DPopOptions` (`SigningAlgorithms` / `ProofTimeWindow` / `NonceLifetime` defaulting to the nine RFC 7518 algorithms / 30 s / 5 min, plus `RequireAllClients`); `AddSchemataAuthorization()` registers it, and `UseDemonstratingProofOfPossession()` customizes it. Per [RFC 9449 §4.3](https://www.rfc-editor.org/rfc/rfc9449.html#section-4.3) steps 1–2 a request carries at most one DPoP header field with a single raw proof JWT; a repeated, empty, or comma-combined value is rejected as `invalid_dpop_proof` (400 on the token and PAR endpoints, a 401 DPoP `WWW-Authenticate` challenge on resource requests) before the proof validator runs.

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
| RFC 7662 §2/§2.1/§2.2 | Introspection: the caller authenticates as a confidential client holding the `e:/Connect/Introspect` permission; an access token reports active only when a verified `aud` value matches the caller's `IntrospectionResourceAudiences` mapping; unknown, invalid, expired, revoked, and audience-inapplicable tokens all return only `{ "active": false }` | Enforced |
| RFC 6750 §3.1, RFC 9449 §7.1 | Presented-credential failures at resource endpoints (unknown or status-invalid row, failed signature, expiry, or `typ` validation, unverifiable identity, wrong audience) stage `invalid_token` on the challenge of the scheme the credential was presented with, indistinguishable across failure causes; requests without credentials keep bare challenges | Enforced |
| RFC 8252 §7.3/§8.3 | Redirect URI matching is exact ordinal string comparison; the single exemption is the loopback port: on `http` URIs whose host is the same loopback IP literal (`127.0.0.1` or `[::1]`) only the explicit port may differ, while scheme spelling, host literal, path, query, and percent encoding must match byte for byte. URIs carrying userinfo or a fragment never match, and registration rejects both with `invalid_redirect_uri`; `localhost` stays excluded per §8.3 | Enforced |
| RFC 9068 §2.1/§2.2, RFC 8707 §2 | Access token `typ: at+jwt`; `aud` = `DefaultResource ?? Issuer` when no resource parameter is sent | Enforced |
| OIDC Core §2 | ID token `aud` = `client_id` | Enforced |
| RFC 9068 §4/§5 | Resource-server validation: presented access tokens pass signature and the `aud` claim must name this server itself (`ResourceAudiences`, defaulting to the non-blank default resource and issuer); tokens audience-restricted to external resources are rejected with `invalid_token`. The profile is read from the cryptographically verified token — the inner signed JWT of a nested encrypted token — and must carry `typ: at+jwt` (or `application/at+jwt`); the outer header alone neither rescues a wrong inner type nor rejects a correct one | Enforced |
| RFC 7517 §4.5 | JWKS publishes every valid and retired issuer signing row with its `kid`; multi-key sets require `kid` on each row | Enforced |
| RFC 9700 §4.16 | Rendered pages carry `X-Frame-Options: DENY` and `CSP: frame-ancestors 'self'` | Enforced |
| RFC 9700 §2.6, RFC 10017 §6.3.3.4 | CORS on public token endpoints | Application responsibility — `SchemataCorsFeature` wires app-level CORS; the host decides origins and reachable endpoints |
| Front-Channel Logout §2 | `iss` and `sid` appended to `frontchannel_logout_uri` as a pair | Enforced |
| Back-Channel Logout §2.4 | Logout token `typ: logout+jwt` | Enforced |
| RP-Initiated Logout §2 | OP session invalidation before RP notifications; failure closes the logout | Enforced |

#### OP session identity binding

`IOpSessionService` resolves one canonical session identifier from collected evidence: a verified
host ticket (the principal's `sid` claim) outranks every other source, and a fresh identifier is
minted only under an authenticated issuance basis — an anonymous OAuth request, a raw auxiliary
cookie, or a synthetic bearer principal alone never establishes an OP session. The browser
mirror cookie is bound to the subject it was persisted for: same-user re-login keeps the
observable OP state stable, while an account switch (A to B) never reuses the previous account's
session and rotates the OP user-agent state. Clearing records the cleared state for the current
request, so subsequent issuance in the same request cannot resurrect the cleared identifier.
Token-endpoint grants keep their validated lineage from the request properties; the session
service is only consulted at the interactive login callback.
| OIDC DCR §2-§3 | Dynamic registration: full metadata validation, 201 creation with paired registration access token + `registration_client_uri`, Bearer read-back and replace; registration requests pass an initial access token gate satisfied by a host-supplied `IInitialAccessTokenValidator` (anonymous requests rejected with 401); software statements apply to both create and replace when the host supplies a trusting validator, are unapproved otherwise, and their trusted claims take precedence while the submitted statement string is stored and echoed verbatim; OAuth-only registrations (no `openid` scope, redirect URIs, response types, `authorization_code`/`implicit` grants, or OIDC-only metadata such as subject/userinfo algorithms, `default_max_age`, `require_auth_time`, `default_acr_values`, `initiate_login_uri`, or logout URIs/session flags) may omit `redirect_uris` and carry no `authorization_code`/`code` defaults; malformed bodies and wrongly-typed fields return `invalid_client_metadata`/`invalid_software_statement` JSON | Enforced (registration surface) |
| RFC 8707 §3 | `resource` at the authorize and token endpoints: §2 syntax validation (`invalid_target`), code-payload and refresh-subset grant consistency, access-token `aud` restriction, introspection echo | Enforced |
| RFC 9396 §§6-7, §9 | `authorization_details` is validated against registered type descriptors and the client's `authorization_details_types` subset (§10). At code exchange and refresh, `IAuthorizationDetailTypeDescriptor.Narrow` owns the type-specific semantic comparison; there is no generic JSON-subset fallback. An omitted token-request parameter retains the grant carried by the code or refresh token, while an accepted narrowing becomes the actual set in the token response JSON array, access-token claim, successor refresh-token lineage, and resource-filtered introspection response. Narrowing never rewrites the original authorization record or authorization-code grant. Without `UseRichAuthorizationRequests()` the bound parameter remains inert and reaches no grant. | Enforced (behind `UseRichAuthorizationRequests()`) |
| OIDC Core §5.3/§5.5 | UserInfo endpoint over an OpenID Connect bearer token; the configured OAuth profile extension also serves authorized end-user OAuth grants without `openid`, while application grants are rejected by persisted subject provenance; `acr_values` voluntary satisfiability per §5.5.1.1 (level walk, stronger performed authentication covers a weaker request); `claims` request parameter with `userinfo`/`id_token` members parsed and validated (`essential`/`value`/`values`), pinned `sub` mismatch handling, Essential `acr` enforcement, ID-token claim projection, private access-token carriage of requested UserInfo names, discovery `claims_parameter_supported`, and signed or encrypted UserInfo responses per §5.3.2 | Enforced for OpenID Connect; OAuth UserInfo is a Schemata extension |
| RFC 7523 | Assertion client authentication (`client_secret_jwt`, `private_key_jwt`); §3.1 `jwt-bearer` grant anchored on the `JwtBearerTrustedIssuers` table — an assertion issuer without an entry is rejected, and an empty table leaves the grant unusable | Enforced |
| RFC 9449 | DPoP proof validation, the [§4.3](https://www.rfc-editor.org/rfc/rfc9449.html#section-4.3) at-most-one DPoP header field rule (`invalid_dpop_proof` on a repeated, empty, or comma-combined value), key-bound tokens, server nonces, discovery metadata, `dpop_jkt` authorize binding, and the [§10.1](https://www.rfc-editor.org/rfc/rfc9449.html#section-10.1) PAR header composition that validates `POST {issuer}/Connect/Par` proofs (no nonce), derives the stored `dpop_jkt` when absent, and rejects a header/parameter mismatch as `invalid_dpop_proof` | Enforced (behind `UseDemonstratingProofOfPossession()`) |

## Caveats

- Token cleanup needs `SchemataSchedulingFeature` and a registered token repository.
- DPoP proof replay markers and server-provided nonces use the conditional operations of
  `ICacheProvider`: proof markers under direct cache keys, server nonces through the cache-backed
  `nonce` token-store slot. Multi-instance deployments require a shared atomic backend, such as
  `RedisCacheProvider`.

## See also

- [OIDC Server cookbook](../cookbook/oidc-server.md) — seed a client and drive the code + PKCE flow
- [Identity](identity.md) — the user store the bridge reads
- [Authorization guide](../guides/authorization.md) — a minimal client-credentials smoke test
