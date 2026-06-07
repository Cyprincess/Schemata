# Authorization

`Schemata.Authorization.Foundation` provides an OAuth 2.0 / OpenID Connect authorization server compliant with [RFC 6749](https://www.rfc-editor.org/rfc/rfc6749.html) and [OpenID Connect Core 1.0](https://openid.net/specs/openid-connect-core-1_0.html). The core feature is generic over four entity types (`TApp`, `TAuth`, `TScope`, `TToken`) and runs at priority 450,000,000. A bridge feature (`SchemataAuthorizationIdentityFeature`) wires Identity's `ISubjectProvider` into the claims pipeline at priority 450,100,000.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Authorization.Skeleton` | `Entities/` — `SchemataApplication`, `SchemataAuthorization`, `SchemataScope`, `SchemataToken` |
| `Schemata.Authorization.Skeleton` | `Advisors/` — advisor interfaces (`ITokenRequestAdvisor`, `IClaimsAdvisor`, `IDestinationAdvisor`, etc.) |
| `Schemata.Authorization.Foundation` | `Extensions/SchemataBuilderExtensions.cs` — two `UseAuthorization` overloads |
| `Schemata.Authorization.Foundation` | `Features/SchemataAuthorizationFeature.cs` — priority 450,000,000 |
| `Schemata.Authorization.Identity` | `Features/SchemataAuthorizationIdentityFeature.cs` — priority 450,100,000 (bridge) |

## Mechanism walkthrough

### 1. Enable the feature

Two overloads are available:

```csharp
// Default entity types
builder.UseSchemata(schema => {
    schema.UseIdentity();
    var auth = schema.UseAuthorization(o => {
        o.Issuer          = "https://auth.example.com";
        o.SigningKey       = myRsaKey;
        o.SigningAlgorithm = "RS256";
    });
    auth.UseCodeFlow();
    auth.UseRefreshTokenFlow();
    auth.UseEndSession();
});

// Custom entity types
schema.UseAuthorization<MyApp, MyAuth, MyScope, MyToken>(configure);
```

`UseAuthorization` stores the `Action<SchemataAuthorizationOptions>` in `Configurators`, registers the OIDC discovery and JWKS endpoints via `WellKnownOptions`, calls `builder.AddFeature<SchemataAuthorizationFeature<...>>()`, and returns a `SchemataAuthorizationBuilder` for chaining flow extensions.

### 2. What the feature registers

`SchemataAuthorizationFeature<TApp, TAuth, TScope, TToken>` (priority 450,000,000) depends on `SchemataAuthenticationFeature`, `SchemataTransportHttpFeature`, and `SchemataWellKnownFeature`. It:

- Validates `SchemataAuthorizationOptions` at startup (`SigningKey`, `SigningAlgorithm`, and `Issuer` are required).
- Collects and sorts `IAuthorizationFlowFeature` instances from `Configurators`, then calls `ConfigureServices` on each.
- Adds a `SchemataExtensionPart` and inserts `OAuthRequestBinderProvider` into MVC's model binder chain.
- Registers `IDiscoveryAdvisor`, `IClientAuthentication<TApp>` (Basic + Post), `IClientAuthenticationService<TApp>`.
- Registers `ITokenRequestAdvisor<TApp>` for endpoint permission, grant permission, and scope validation.
- Registers `IClaimsAdvisor` for audience claims and pairwise subject projection.
- Registers `IDestinationAdvisor` for subject, profile, email, phone, address, and role claim routing.
- Registers `DiscoveryHandler<TScope>`, `TokenService`, `ISubjectIdentifierService`.
- Registers managers: `IApplicationManager<TApp>`, `IScopeManager<TScope>`, `IAuthorizationManager<TAuth>`, `ITokenManager<TToken>`.
- Adds two authentication schemes (bearer and authorization code) via `services.AddAuthentication()`.
- Registers `TokenCleanupJob<TToken>` as a transient `IScheduledJob` and adds an hourly `CronSchedule("0 * * * *")` entry to `SchemataSchedulingOptions.Jobs`.
- Registers `BackChannelLogoutJob` as a transient `IScheduledJob`. `BackChannelLogoutService<TApp, TToken>` builds the per-RP claim set, signs the logout JWT, and triggers `BackChannelLogoutJob` through `IScheduler.TriggerAsync`.

### 3. Identity bridge

`SchemataAuthorizationIdentityFeature` (priority 450,100,000) bridges Identity into the authorization pipeline. It depends on both `SchemataAuthorizationFeature` and `SchemataIdentityFeature` via string-based `[DependsOn]` attributes (soft dependencies across assemblies). When both are present, it registers `IdentitySubjectProvider<TUser>` as `ISubjectProvider` and adds `AdviceSubjectClaims` to the `IClaimsAdvisor` pipeline.

### 4. Flow extensions

Each flow is enabled by calling a method on the `SchemataAuthorizationBuilder` returned by `UseAuthorization()`:

| Method | Grant type / endpoint |
| --- | --- |
| `UseCodeFlow()` | `authorization_code`, `GET|POST /Connect/Authorize` |
| `UseClientCredentialsFlow()` | `client_credentials` |
| `UseRefreshTokenFlow()` | `refresh_token` |
| `UseDeviceFlow()` | `urn:ietf:params:oauth:grant-type:device_code`, `POST /Connect/Device` |
| `UseIntrospection()` | `POST /Connect/Introspect` (RFC 7662) |
| `UseRevocation()` | `POST /Connect/Revoke` (RFC 7009) |
| `UseEndSession()` | `GET|POST /Connect/EndSession` (OIDC RP-Initiated Logout) |

### 5. Discovery endpoints

`GET /.well-known/openid-configuration` and `GET /.well-known/jwks` are registered as Minimal API routes via `SchemataWellKnownFeature`, independent of MVC. `IDiscoveryAdvisor` populates the document; each flow feature adds its own entries (grant types, endpoints, capabilities).

### 6. Token endpoint pipeline

`POST /Connect/Token` dispatches by `grant_type` to `IGrantHandler` implementations. Before each handler, `ITokenRequestAdvisor<TApp>` runs three advisors in order:

| Advisor | Responsibility |
| --- | --- |
| `AdviceTokenEndpointPermission` | Validates the client has endpoint permission |
| `AdviceTokenGrantPermission` | Verifies the client has permission for the requested grant type |
| `AdviceTokenScopeValidation` | Validates requested scopes against client permissions |

### 7. Claims pipeline

After grant-specific processing, `IClaimsAdvisor` enriches the principal and `IDestinationAdvisor` routes each claim to access tokens, identity tokens, or both.

## Extension points

| Interface | Purpose |
| --- | --- |
| `IAuthorizationFlowFeature` | Add a new grant type or endpoint. Register via `SchemataAuthorizationBuilder`. |
| `IGrantHandler` | Implement a custom grant type. Register via `services.TryAddEnumerable`. |
| `IClaimsAdvisor` | Add or transform claims before token issuance. |
| `IDestinationAdvisor` | Control which tokens receive a given claim. |
| `IDiscoveryAdvisor` | Populate the OIDC discovery document. |
| `IClientAuthentication<TApp>` | Add a custom client authentication method. |
| `ISubjectProvider` | Provide the subject identifier for a principal (wired by the Identity bridge). |

## Design motivation

The four-entity generic design (`TApp`, `TAuth`, `TScope`, `TToken`) lets you replace any entity with a domain-specific subclass without forking the feature. The bridge feature (`SchemataAuthorizationIdentityFeature`) is a separate assembly so that `Schemata.Authorization.Foundation` has no compile-time dependency on `Schemata.Identity.Foundation` — the bridge uses string-based `[DependsOn]` and resolves the user type at runtime.

The `IAuthorizationFlowFeature` pattern keeps each grant type isolated. Adding a new flow does not require modifying the core feature.

## Caveats

- `SchemataAuthorizationFeature` has `Priority = Orders.Extension + 50_000_000 = 450_000_000`. The Identity bridge sits at `450_100_000`. See [Built-in Features](core/built-in-features.md) for the full priority table.
- `SchemataAuthorizationOptions.SigningKey`, `SigningAlgorithm`, and `Issuer` are validated at startup via `PostConfigure`. Missing any of them throws `InvalidOperationException` before the host starts.
- The feature depends on `SchemataWellKnownFeature` for the discovery and JWKS endpoints. This feature is pulled in automatically via `[DependsOn<SchemataWellKnownFeature>]`.
- `SchemataAuthorizationIdentityFeature` uses string-based `[DependsOn]` attributes because it cannot reference `SchemataAuthorizationFeature<...>` or `SchemataIdentityFeature<...>` directly (different assemblies, open generics). If either dependency is absent, the bridge silently does nothing.
- Token pruning and back-channel logout run as `IScheduledJob` implementations. Both require `IScheduler` and `SchemataSchedulingFeature` to be registered. The `TToken` / `TApp` repositories must be registered before the authorization feature runs.

## See also

- [Built-in Features](core/built-in-features.md) — feature priority table
- [Identity](identity.md) — ASP.NET Core Identity integration
- [Security](security.md) — access providers and row-level filtering
- [Advice Pipeline](core/advice-pipeline.md) — how advisor pipelines execute and short-circuit
