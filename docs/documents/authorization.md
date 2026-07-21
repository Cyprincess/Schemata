# Authorization

`Schemata.Authorization.Foundation` is a hand-rolled OAuth 2.0 / OpenID Connect authorization
server. It builds on `Microsoft.IdentityModel` for key material and JWT handling but pulls in no
external server framework. The core feature is generic over four entity types — `TApp`, `TAuth`,
`TScope`, `TToken` — and runs at priority 450,000,000. Flows are opt-in: `UseAuthorization()`
registers the core, and each `Use*Flow` / `Use*` call on the returned builder adds one
`IAuthorizationFlowFeature`.

## Where the code lives

| Package                             | Key files                                                                                                                                                                                                                                                                        |
| ----------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Authorization.Skeleton`   | `Entities/{SchemataApplication,SchemataAuthorization,SchemataScope,SchemataToken}.cs`, `Advisors/`, `Contexts/`, `Handlers/`, `Managers/`, `Services/IClientAuthentication.cs`, `ISubjectProvider.cs`                                                                            |
| `Schemata.Authorization.Foundation` | `Extensions/SchemataBuilderExtensions.cs` (`UseAuthorization`), `Extensions/SchemataAuthorizationBuilderExtensions.cs` (flow methods), `Features/`, `Controllers/ConnectController*.cs`, `Authentication/SchemataAuthorizationOptions.cs`, `Managers/`, `Services/`, `Advisors/` |
| `Schemata.Authorization.Identity`   | `Features/SchemataAuthorizationIdentityFeature.cs`, `IdentitySubjectProvider.cs`, `Advisors/AdviceClaimsSubject.cs`, the `UseIdentity()` builder extension                                                                                                                       |

## Enabling the server

```csharp
builder.UseSchemata(schema => {
    schema.UseIdentity();

    schema.UseAuthorization(o => {
              o.Issuer = "https://auth.example.com";
              o.AddEphemeralSigningKey();        // dev only; load a persisted key in production
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
`SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken>` for chaining.

## What the core feature registers

`SchemataAuthorizationFeature<TApp, TAuth, TScope, TToken>` (`Priority = Orders.Extension +
50_000_000 = 450_000_000`) depends on `SchemataAuthenticationFeature`,
`SchemataTransportHttpFeature`, and `SchemataWellKnownFeature`. `ConfigureServices`:

- Validates `SchemataAuthorizationOptions` in `PostConfigure`: `SigningKey`, `SigningAlgorithm`,
  and `Issuer` are required, and `EncryptionAlgorithm` is required when `EncryptionKey` is set.
  Any missing value throws `InvalidOperationException`.
- Collects the registered `IAuthorizationFlowFeature` instances, sorts them by `Order`, and calls
  `ConfigureServices` on each — this is how flow methods contribute their handlers and advisors.
- Adds the controller as a `SchemataApplicationPart` and inserts `OAuthRequestBinderProvider` at
  the front of the MVC model-binder chain so OAuth form/query parameters bind to the OAuth model
  types instead of the default MVC binders.
- Registers the four managers (scoped): `IApplicationManager<TApp>`, `IScopeManager<TScope>`,
  `IAuthorizationManager<TAuth>`, `ITokenManager<TToken>`.
- Registers client authentication: `ClientSecretBasicAuthentication<TApp>` and
  `ClientSecretPostAuthentication<TApp>` as `IClientAuthentication<TApp>`, plus
  `IClientAuthenticationService<TApp>`.
- Registers the advisor families (see below), `DiscoveryHandler<TScope>`, `TokenService`, and
  `ISubjectIdentifierService`.
- Adds two authentication schemes via `AddAuthentication()`: `BearerScheme`
  (`SchemataAuthenticationHandler<TApp, TToken>`) and `CodeScheme`
  (`SchemataAuthorizationCodeHandler<TApp, TToken>`).
- Registers `TokenCleanupJob<TToken>` and schedules it through the Scheduling job model — see
  below.

## Endpoints

`ConnectController` is routed at `~/Connect`. The actions a deployment actually serves depend on
which flow methods are enabled, but the routes are fixed:

| Method                    | Route                 | Action                                                | Spec                     |
| ------------------------- | --------------------- | ----------------------------------------------------- | ------------------------ |
| `GET` / `POST`            | `/Connect/Authorize`  | `AuthorizeGet` / `AuthorizePost`                      | RFC 6749 §4.1            |
| `POST`                    | `/Connect/Token`      | `Token`                                               | RFC 6749 §3.2            |
| `POST`                    | `/Connect/Device`     | `Device`                                              | RFC 8628                 |
| `GET` / `POST` / `DELETE` | `/Connect/Interact`   | `Interact` / `ApproveInteraction` / `DenyInteraction` | consent interaction      |
| `POST`                    | `/Connect/Introspect` | `Introspect`                                          | RFC 7662                 |
| `POST`                    | `/Connect/Revoke`     | `Revoke`                                              | RFC 7009                 |
| `GET` / `POST`            | `/Connect/Profile`    | `Profile` (bearer-authorized)                         | OIDC Core §5.3 UserInfo  |
| `GET` / `POST`            | `/Connect/EndSession` | `EndSessionGet` / `EndSessionPost`                    | OIDC RP-Initiated Logout |

`GET /.well-known/openid-configuration` and `GET /.well-known/jwks` are mapped through
`WellKnownOptions` (the `SchemataWellKnownFeature` pipeline), backed by `DiscoveryHandler<TScope>`.
Each `IDiscoveryAdvisor` contributes a slice of the discovery document, so the advertised grant
types and endpoints reflect exactly which flows are enabled.

## Flows

Each method on `SchemataAuthorizationBuilder` adds one or more flow features. The grant types and
endpoints below are the ones the code implements:

| Builder method               | Grant type / endpoint                                             | Flow feature                                                            |
| ---------------------------- | ----------------------------------------------------------------- | ----------------------------------------------------------------------- |
| `UseCodeFlow()`              | `authorization_code` (+ PKCE), `/Connect/Authorize`               | `AuthorizationCodeFlowFeature` (+ `TokenFeature`, `InteractionFeature`) |
| `UseClientCredentialsFlow()` | `client_credentials`                                              | `ClientCredentialsFlowFeature`                                          |
| `UseRefreshTokenFlow()`      | `refresh_token`                                                   | `RefreshTokenFlowFeature`                                               |
| `UseDeviceFlow()`            | `urn:ietf:params:oauth:grant-type:device_code`, `/Connect/Device` | `DeviceFlowFeature` (+ `InteractionFeature`)                            |
| `UseTokenExchange()`         | `urn:ietf:params:oauth:grant-type:token-exchange`                 | `TokenExchangeFeature`                                                  |
| `UseIntrospection()`         | `/Connect/Introspect` (RFC 7662)                                  | `IntrospectionFeature`                                                  |
| `UseRevocation()`            | `/Connect/Revoke` (RFC 7009)                                      | `RevocationFeature`                                                     |
| `UseUserInfo()`              | `/Connect/Profile` (OIDC UserInfo)                                | `UserInfoFeature`                                                       |
| `UseEndSession()`            | `/Connect/EndSession` (RP-Initiated Logout)                       | `EndSessionFeature`                                                     |
| `UseFrontChannelLogout()`    | front-channel logout metadata                                     | `FrontChannelLogoutFeature`                                             |
| `UseBackChannelLogout()`     | back-channel logout queue + notifier                              | `BackChannelLogoutFeature`                                              |

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
| `IDiscoveryAdvisor`                                                                                                                                                              | —                  | Populate the discovery document                             | `AdviceDiscoveryBase` plus one per flow (`AdviceDiscoveryCodeFlow`, `AdviceDiscoveryRefreshToken`, `AdviceDiscoveryDeviceFlow`, `AdviceDiscoveryIntrospection`, `AdviceDiscoveryRevocation`, `AdviceDiscoveryUserInfo`, `AdviceDiscoveryEndSession`, …)                                                          |
| `IClaimsAdvisor`                                                                                                                                                                 | —                  | Enrich the principal before token issuance                  | `AdviceClaimsAudience`, `AdviceClaimsPairwise<TApp>`, and `AdviceClaimsSubject` (Identity bridge)                                                                                                                                                                                                                |
| `IDestinationAdvisor`                                                                                                                                                            | —                  | Route each claim to access token, ID token, and/or UserInfo | `AdviceDestinationSubject`, `Advice{Profile,Email,Phone,Address,Role}ClaimDestination`                                                                                                                                                                                                                           |
| `ITokenRequestAdvisor<TApp>`                                                                                                                                                     | `TApp`             | Validate the token request                                  | `AdviceRequestEndpointPermission`, `AdviceRequestGrantPermission`, `AdviceRequestScopeValidation`                                                                                                                                                                                                                |
| `IAuthorizeAdvisor<TApp>`                                                                                                                                                        | `TApp`             | Validate the authorize request                              | `AdviceAuthorizeClientAndRedirect`, `AdviceAuthorizeEndpointPermission`, `AdviceAuthorizeGrantPermission`, `AdviceAuthorizeScopeValidation`, `AdviceAuthorizePkce`, `AdviceAuthorizeNonce`, `AdviceAuthorizePrompt`, `AdviceAuthorizeResponseMode`, `AdviceAuthorizeConsent`, `AdviceAuthorizeAutoApproveSignIn` |
| `ICodeExchangeAdvisor` / `IRefreshTokenAdvisor` / `IIntrospectionAdvisor` / `IRevocationAdvisor` / `IUserInfoAdvisor` / `IDeviceAuthorizeAdvisor` / `IDeviceCodeExchangeAdvisor` | `TApp`(, `TToken`) | Validate each endpoint's request                            | `AdviceCodeExchange*`, `AdviceRefreshTokenValidation`, `AdviceIntrospection*`, `AdviceRevocation*`, `AdviceUserInfoOpenIdRequirement`, `AdviceDevice*`                                                                                                                                                           |

## Permissions

A client's capabilities are a list of permission strings on `SchemataApplication.Permissions`,
prefixed per `SchemataConstants.PermissionPrefixes`:

| Prefix | Constant    | Example                                                           |
| ------ | ----------- | ----------------------------------------------------------------- |
| `e:`   | `Endpoint`  | `e:/Connect/Token`, `e:/Connect/Authorize`                        |
| `g:`   | `GrantType` | `g:authorization_code`, `g:client_credentials`, `g:refresh_token` |
| `s:`   | `Scope`     | `s:openid`, `s:profile`                                           |

`IApplicationManager<TApp>.HasPermissionAsync(app, permission, ct)` is the lookup the permission
advisors use.

## Managers

The managers are open-generic over their entity type and take a `CancellationToken` on every
method. Key lookups:

- `IApplicationManager<TApp>`: `FindByClientIdAsync`, `ValidateClientSecretAsync`,
  `HasPermissionAsync`.
- `IScopeManager<TScope>`: `FindByNameAsync`, `ListAsync`.
- `IAuthorizationManager<TAuth>`: `CreateAsync` and lifecycle queries.
- `ITokenManager<TToken>`: `FindByReferenceIdAsync`, `FindByNameAsync`, `ListBySubjectAsync`,
  `ListBySessionAsync`, `CreateAsync`, `RevokeByAuthorizationAsync`, and `PruneAsync(threshold,
ct)` for cleanup.

## Background jobs

Token cleanup runs through the Scheduling job model. The core feature registers
`TokenCleanupJob<TToken>` through `services.AddScheduledJob<TokenCleanupJob<TToken>>()` (transient
registration plus a known-only job entry) and adds a `JobRegistration` to
`SchemataSchedulingOptions.Jobs` with a
`CronSchedule("0 * * * *")` — hourly at minute 0. The job calls
`ITokenManager<TToken>.PruneAsync`. This needs `SchemataSchedulingFeature` and the `TToken`
repository registered.

`UseBackChannelLogout()` registers `BackChannelLogoutFeature`, which wires
`BackChannelLogoutService<TApp, TToken>` as the `ILogoutNotifier`, an `HttpClient`, and a transient
`BackChannelLogoutJob`. The service builds the per-RP logout token, signs it, and triggers the job;
there is no cron schedule on it.

## Identity bridge

`Schemata.Authorization.Identity` connects the authorization server to ASP.NET Core Identity. It is
not automatic — you opt in by calling `.UseIdentity()` on the `SchemataAuthorizationBuilder`, which
adds `SchemataAuthorizationIdentityFeature` (`Priority = Orders.Extension + 50_100_000 =
450_100_000`). The feature declares `[DependsOn(typeof(SchemataAuthorizationFeature<,,,>))]` and
`[DependsOn(typeof(SchemataIdentityFeature<,,,>))]` — open-generic type references that match any
closed instantiation. The package references `Schemata.Identity.Foundation` (not
`Schemata.Identity.Skeleton`) so the feature types are reachable. At configure time it discovers the
registered user type from
the `IUserValidator<>` descriptor, registers `IdentitySubjectProvider<TUser>` as `ISubjectProvider`,
and adds `AdviceClaimsSubject` to the `IClaimsAdvisor` chain. `IdentitySubjectProvider` projects
`sub`, `preferred_username`, `email` (+`email_verified`), `phone_number` (+`phone_number_verified`),
`nickname`, and `role` claims from the user.

## Entity types

All four entities use `Guid Uid` as the primary key and carry `[PrimaryKey(nameof(Uid))]`:

| Entity                  | Table                    | Canonical name                   | Notable properties                                                                                                                                      |
| ----------------------- | ------------------------ | -------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `SchemataApplication`   | `SchemataApplications`   | `applications/{application}`     | `ClientId`, `ClientSecret`, `ClientType`, `ConsentType`, `RequirePkce`, `RedirectUris`, `PostLogoutRedirectUris`, `Permissions`, `BackChannelLogoutUri` |
| `SchemataAuthorization` | `SchemataAuthorizations` | `authorizations/{authorization}` | `Application`, `Subject`, `Type`, `Status`, `Scopes`, `CodeChallengeMethod`                                                                             |
| `SchemataScope`         | `SchemataScopes`         | `scopes/{scope}`                 | `Name`, `Resources`                                                                                                                                     |
| `SchemataToken`         | `SchemataTokens`         | `tokens/{token}`                 | `Application`, `Authorization`, `Subject`, `SessionId`, `Type`, `Status`, `Format`, `ReferenceId`, `Payload`, `ExpireTime`                              |

## SchemataAuthorizationOptions

Key material is required; lifetimes and formats have defaults:

| Property                                    | Default          | Notes                                                       |
| ------------------------------------------- | ---------------- | ----------------------------------------------------------- |
| `Issuer`                                    | —                | Required (`iss` claim, discovery base URL)                  |
| `SigningKey` / `SigningAlgorithm`           | —                | Required; `AddEphemeralSigningKey(alg)` generates a dev key |
| `EncryptionKey` / `EncryptionAlgorithm`     | `null`           | Optional JWE; `AddEphemeralEncryptionKey(alg)` available    |
| `AccessTokenFormat`                         | `Jwe`            | `Jwt`, `Jwe`, or `Reference`                                |
| `RefreshTokenFormat`                        | `Reference`      |                                                             |
| `AccessTokenLifetime` / `IdTokenLifetime`   | 1 hour           |                                                             |
| `RefreshTokenLifetime`                      | 14 days          |                                                             |
| `AuthorizationCodeLifetime`                 | 10 minutes       |                                                             |
| `DeviceCodeLifetime` / `DeviceCodeInterval` | 15 minutes / 5 s |                                                             |
| `SubjectType`                               | `Public`         | `Public` or `Pairwise` (with `PairwiseSalt`)                |
| `DeviceVerificationUri`                     | `null`           | Required by the device flow                                 |
| `BearerScheme` / `CodeScheme`               | scheme constants | Authentication scheme names                                 |

`PermitResponseType(...)` and `AddEphemeral*Key(...)` are fluent helpers on the options object.

## Extension points

| Interface                                | Purpose                                                        |
| ---------------------------------------- | -------------------------------------------------------------- |
| `IAuthorizationFlowFeature`              | Add a grant type or endpoint as an ordered flow feature.       |
| `IGrantHandler`                          | Implement a token-endpoint grant.                              |
| `IClaimsAdvisor` / `IDestinationAdvisor` | Add claims and route them to tokens.                           |
| `IDiscoveryAdvisor`                      | Add discovery-document entries.                                |
| `IClientAuthentication<TApp>`            | Add a client authentication method.                            |
| `ISubjectProvider`                       | Provide the subject identifier (wired by the Identity bridge). |

## Caveats

- The options validation runs in `PostConfigure`, so a missing `SigningKey`, `SigningAlgorithm`, or
  `Issuer` surfaces as an `InvalidOperationException` when the options are first resolved.
- The bridge is opt-in. Without `.UseIdentity()` on the authorization builder, tokens carry only
  the base claims; user claims do not appear.
- The device flow requires `DeviceVerificationUri`.
- Token cleanup needs `SchemataSchedulingFeature` and a registered `TToken` repository.

## See also

- [OIDC Server cookbook](../cookbook/oidc-server.md) — seed a client and drive the code + PKCE flow
- [Identity](identity.md) — the user store the bridge reads
- [Authorization guide](../guides/authorization.md) — a minimal client-credentials smoke test
