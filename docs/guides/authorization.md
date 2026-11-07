# Authorization

Stand up an OAuth 2.0 / OpenID Connect authorization server on the Student app and obtain an access
token through the client-credentials flow. This guide builds on [Identity](identity.md).

Examples are source-traced integration fragments, not a compiled or executed sample. Keep the
prerequisite web project's implicit `System`, threading, task, ASP.NET Core, and DI imports.
JSON responses below are illustrative, not captured run output.

## Add the package

`Schemata.Application.Complex.Targets` already includes `Schemata.Authorization.Foundation`. The
Identity bridge, selected with `.UseIdentity<SchemataUser>()` on the authorization builder, lives in a
separate package:

```shell
dotnet add package --prerelease Schemata.Authorization.Identity
```

When composing manually, also add `Schemata.Authorization.Foundation` itself. Application provisioning through
SCIM uses the separate `Schemata.Authorization.Scim` package:

```shell
dotnet add package --prerelease Schemata.Authorization.Scim
```

## Enable the server

Add `UseSecurity()` and `UseAuthorization()` inside the `UseSchemata` block and chain the flows
you need. The issuer identifies the server; its signing keys are security rows seeded below.

```csharp
using Schemata.Identity.Skeleton.Entities;

schema.UseIdentity();

schema.UseSecurity();           // security rows, secret verification, token stores

schema.UseAuthorization(o => {
          o.Issuer = "https://auth.example.com";
      })
      .UseClientCredentialsFlow()
      .UseAuthorizationCodeFlow()
      .UseRefreshTokenFlow()
      .UseIdentity<SchemataUser>();
```

`UseAuthorization()` returns a `SchemataAuthorizationBuilder`. Each method below adds one flow:

Use `IApplicationManager<TApp>`, `IScopeManager<TScope>`, and `ITokenStore<TToken>` for native administration. Use dynamic client registration for OAuth clients and SCIM for external Application provisioning; these are HTTP protocol surfaces rather than native Resource CRUD transports.

| Method                       | Grant / endpoint                                                         |
| ---------------------------- | ------------------------------------------------------------------------ |
| `UseAuthorizationCodeFlow()`              | `authorization_code` with PKCE — `/Connect/Authorize` + `/Connect/Token` |
| `UseClientCredentialsFlow()` | `client_credentials` — `/Connect/Token`                                  |
| `UseResourceOwnerPasswordFlow()` | historical `password` grant — `/Connect/Token`                      |
| `UseRefreshTokenFlow()`      | `refresh_token` — `/Connect/Token`                                       |
| `UseDeviceFlow()`            | device code — `/Connect/Device`                                          |
| `UseIntrospection()`         | `/Connect/Introspect`                                                    |
| `UseRevocation()`            | `/Connect/Revoke`                                                        |
| `UseUserInfo()`              | `/Connect/Profile`                                                       |
| `UseEndSession()`            | `/Connect/EndSession`                                                    |
| `UsePairwiseSubjects()`      | pairwise subject identifiers — OIDC Core §8                              |

`Issuer` is required; the host throws `InvalidOperationException` when it is missing. Token
issuance loads the signing rows under the issuer and throws `InvalidOperationException` when
none is valid, so seed one below. The `.UseIdentity<SchemataUser>()` call on the authorization builder
is what wires user claims (`sub`, `email`, `role`, …) into issued tokens.

### Historical password compatibility

`UseResourceOwnerPasswordFlow()` enables the RFC 6749 §4.3 `password` grant and advertises it in
discovery. Without that registration, the token endpoint returns `unsupported_grant_type` and
discovery omits the grant. Each client must list `password` in `GrantTypes` and retain the token
endpoint permission and scopes needed by the request.

[RFC 9700 §2.4](https://www.rfc-editor.org/rfc/rfc9700.html#section-2.4) says this grant MUST NOT be
used because the client handles the resource owner's password, expanding credential exposure and
excluding interactive multi-factor authentication. This feature is an explicit historical
compatibility exception, not RFC 9700 Best Current Practice compliant behavior. Keep it disabled
outside a bounded migration requirement.

The authorization Identity bridge registers `IResourceOwnerPasswordValidator`. It applies Identity
password verification, lockout, administrative admission, and two-factor rejection, then uses the
canonical claims factory. A host without that bridge must register its own validator. A missing
validator, rejected credentials, or a principal without `sub` returns `invalid_grant`.

The grant issues an access token and can issue a refresh token for `offline_access`; it does not
issue an ID token or a host authentication cookie. The handler passes the password only to the
validator and does not place it in sign-in properties or persisted resource names. Consumers must
also prevent credential logging in their validators and advisors.

## Update the DbContext

Add the authorization and security tables alongside the Identity tables:

```csharp
using Microsoft.EntityFrameworkCore;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Scim.Entities;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options)
{
    public DbSet<Student>               Students       => Set<Student>();
    public DbSet<SchemataUser> Users => Set<SchemataUser>();
    public DbSet<SchemataRole> Roles => Set<SchemataRole>();
    public DbSet<SchemataUserClaim> UserClaims => Set<SchemataUserClaim>();
    public DbSet<SchemataRoleClaim> RoleClaims => Set<SchemataRoleClaim>();
    public DbSet<SchemataUserRole> UserRoles => Set<SchemataUserRole>();
    public DbSet<SchemataUserLogin> UserLogins => Set<SchemataUserLogin>();
    public DbSet<SchemataUserToken> UserTokens => Set<SchemataUserToken>();
    public DbSet<SchemataApplication>   Applications   => Set<SchemataApplication>();
    public DbSet<SchemataAuthorization> Authorizations => Set<SchemataAuthorization>();
    public DbSet<SchemataScope>         Scopes         => Set<SchemataScope>();
    public DbSet<SchemataSecurity>      Securities     => Set<SchemataSecurity>();
    public DbSet<SchemataToken>         Tokens         => Set<SchemataToken>();
    public DbSet<ScimProvisioningBinding> ProvisioningBindings => Set<ScimProvisioningBinding>();
}
```

Each entity carries `[PrimaryKey(nameof(Uid))]` and its own `[Table]`, so no extra mapping is
needed.

When you register repositories manually, register one for `SchemataSecurity` too;
`SecurityStore<TSecurity>` reads through `IRepository<TSecurity>`.

## Name the framework-owned rows

The framework never generates a resource `Name`; every row created through the repository
pipeline gets one from your code. Seeded rows take an explicit `Name` (shown in the seed below);
the token and authorization rows the server creates during a flow need a repository add advisor,
registered ahead of canonical-name derivation:

```csharp
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

public sealed class AdviceAddResourceName<TEntity> : IRepositoryAddAdvisor<TEntity>
    where TEntity : class
{
    public int Order => 0;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        MutationOptions      options,
        CancellationToken    ct = default)
    {
        if (entity is ICanonicalName named && string.IsNullOrWhiteSpace(named.Name))
            named.Name = $"resource-{Guid.NewGuid():n}";

        return Task.FromResult(AdviseResult.Continue);
    }
}
```

```csharp
schema.ConfigureServices(services => {
    services.TryAddEnumerable(
        ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataToken>, AdviceAddResourceName<SchemataToken>>());
    services.TryAddEnumerable(
        ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataAuthorization>, AdviceAddResourceName<SchemataAuthorization>>());
});
```

`Order = 0` runs before the built-in identifier advisor at 90,000,000 and canonical-name derivation
at 120,000,000, so the latter sees the assigned `Name`. Missing it surfaces
as a missing-`Name` failure on the first issued token. [Getting Started](getting-started.md) walks
the same pattern for your own entities.

## Register a client

Seed a confidential client at startup through `IApplicationManager<SchemataApplication>`. Grant and
scope admission read the typed `GrantTypes`, `ResponseTypes`, and `Scope` fields; `Permissions` is
administrator-only and holds endpoint entries with the `e:` prefix:

```csharp
using System.Security.Cryptography;
using Schemata.Authorization.Foundation.Services;   // SecurityParents
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;

var manager = scope.ServiceProvider
    .GetRequiredService<IApplicationManager<SchemataApplication>>();
var securities = scope.ServiceProvider
    .GetRequiredService<ISecurityStore<SchemataSecurity>>();
var verifier = scope.ServiceProvider
    .GetRequiredService<ISecretVerifier>();

// The server signs every token with its newest valid signing row under the issuer.
var hasSigningKey = false;
await foreach (var _ in securities.ListByParentAsync(
                   "https://auth.example.com", SecurityConstants.Kinds.PrivateKey,
                   SecurityConstants.Usages.Signing, SecurityConstants.Statuses.Valid)) {
    hasSigningKey = true;
    break;
}

if (!hasSigningKey) {
    using var rsa = RSA.Create(2048);
    await securities.CreateAsync(new SchemataSecurity {
        Parent    = "https://auth.example.com",   // SecurityParents.Issuer(o.Issuer)
        Name      = "issuer-signing",
        Kind      = SecurityConstants.Kinds.PrivateKey,
        Usage     = SecurityConstants.Usages.Signing,
        Algorithm = "RS256",                      // SigningAlgorithms.RsaSha256
        Kid       = "student-signing-1",
        Value     = rsa.ExportPkcs8PrivateKeyPem(),
        Status    = SecurityConstants.Statuses.Valid,
    }, default);
}

if (await manager.FindByClientIdAsync("student-app", default) is null)
{
    var app = new SchemataApplication {
        Name          = "student-app",
        ClientId      = "student-app",
        ClientName    = "Student App",
        GrantTypes    = ["client_credentials", "authorization_code", "refresh_token"],
        ResponseTypes = ["code"],
        Scope         = "openid profile",
        RedirectUris  = ["http://localhost:5001/callback"],
        Permissions   = ["e:/Connect/Token"],
    };
    await manager.CreateAsync(app, default);

    // The client authenticates with its newest valid password row; only the PBKDF2 hash is stored.
    await securities.CreateAsync(new SchemataSecurity {
        Parent    = SecurityParents.Application(app),
        Name      = app.ClientId,
        Kind      = SecurityConstants.Kinds.Password,
        Usage     = SecurityConstants.Usages.Authentication,
        Algorithm = SecurityConstants.Algorithms.Pbkdf2,
        Value     = await verifier.HashAsync("secret"),
        Status    = SecurityConstants.Statuses.Valid,
    }, default);
}
```

Manager methods take a `CancellationToken`; pass `default` when seeding.

`SchemataApplication.Name` is the application's resource name, independent of `ClientId`; the
canonical record `applications/student-app` is persisted in the `Application` field of issued
tokens. The access-token audience follows the request instead: adopted resource indicators become
`aud` values verbatim, and without them the claim falls back to `DefaultResource ?? Issuer`. The
ID token audience is the `client_id`, and bearer validation accepts a token whose `aud` names a
configured `ResourceAudiences` entry, the default resource, or the issuer.

## Provision applications through SCIM

Chain `UseScim()` on the authorization builder to expose `/scim/v2/Applications`:

```csharp
schema.UseAuthorization(options => {
          options.Issuer = "https://auth.example.com";
          options.InteractionUri = "https://auth.example.com/interact";
      })
      .UseAuthorizationCodeFlow()
      .UseScim();
```

Register `IScimProvisioningDomainProvider` and the SCIM authentication scheme, and register the
binding repository in the same provider context as the Application and security repositories. The
Application resource uses these schema URIs:

- `https://github.com/Cyprincess/Schemata/scim/schemas/2.0/Application`
- `https://github.com/Cyprincess/Schemata/scim/schemas/2.0/ApplicationAuthorization`

SCIM and dynamic client registration call the same metadata mapper and application manager. SCIM
uses authenticated provisioning domains and write policy; dynamic registration uses initial-access
and registration-access tokens. Managers own native administration; Resource HTTP/gRPC remain
available for application-defined resources, not raw authorization-entity management.

`grantTypes`, `responseTypes`, and `scope` populate the metadata used by the OAuth handlers. They do
not create endpoint permissions. `clientId` is generated and immutable, SCIM `id` is the entity
`Uid`, and your resource-name advisor supplies `Name`. `externalId` is unique within one provisioning
domain and may be reused in another domain.

The ApplicationAuthorization extension contains `permissions`. Omitting it preserves the current
collection. Changing it requires `CanWritePermissionsAsync` on `IScimProvisioningAuthorizer`; a
rejected change fails the whole operation. A delegated caller can replace or clear the collection.

`jwks` is an array of public JWK objects at the top level. The mapper stores the corresponding
`{"keys":[...]}` security document. Private and symmetric keys, client secrets, registration access
tokens, and OAuth tokens are absent from responses. Use the registration and security-management
paths to issue or rotate credentials.

Each Application write stages metadata, public key rows, and its binding in one serializable
transaction and commits once. Delete follows the manager's existing cascade for client credentials,
grants, pairwise mappings, and application tokens.

## Require step-up authentication

Register `IResourceAuthenticationRequirementProvider` to select authentication strength and
recentness from the validated principal, client, resource, and existing request parameters. This
provider returns a multifactor requirement with a five-minute authentication-age bound:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Authorization.Skeleton.Services;

public sealed class ApiAuthenticationRequirements : IResourceAuthenticationRequirementProvider
{
    public Task<ResourceAuthenticationRequirement?> EvaluateAsync(
        ResourceAuthenticationRequirementContext context,
        CancellationToken ct = default)
    {
        if (context.Resource != "https://api.example.com")
            return Task.FromResult<ResourceAuthenticationRequirement?>(null);

        return Task.FromResult<ResourceAuthenticationRequirement?>(
            new(["urn:example:mfa"], TimeSpan.FromSeconds(300)));
    }
}

schema.Services.AddScoped<IResourceAuthenticationRequirementProvider, ApiAuthenticationRequirements>();
```

At a protected resource, an access token whose `acr` or `auth_time` does not satisfy the returned
requirement receives a `401` challenge. Bearer and DPoP challenges carry
`error="insufficient_user_authentication"`, plus `acr_values="urn:example:mfa"` and
`max_age="300"`. The client sends those values to `/Connect/Authorize` as `acr_values` and
`max_age`, then completes interactive authentication with evidence that meets them.

Add `.UseClaimsParameter()` to the authorization-code flow so its authorize advisor merges the
provider's requirements into the login interaction. A request that cannot meet them silently
returns `unmet_authentication_requirements`; an interactive approval is checked again before a
code is issued. A null requirement preserves ordinary `acr_values` behavior, including an
unsatisfied voluntary request.

The login pipeline owns the authentication event and supplies `acr`, `amr`, and `auth_time` through
`IAuthenticationContextProvider`. Code exchange projects that event into the grant and tokens.
Refresh keeps the original `auth_time`; it never records token renewal as fresh user authentication.

## Verify

```shell
dotnet run
```

Client-credentials flow (no user involved):

```shell
curl -X POST http://localhost:5000/Connect/Token \
     -d "grant_type=client_credentials" \
     -d "client_id=student-app" \
     -d "client_secret=secret"
```

```json
{
  "access_token": "eyJhbG...",
  "token_type": "Bearer",
  "expires_in": 3600
}
```

Call a protected endpoint with the token:

```shell
curl http://localhost:5000/v1/students \
     -H "Authorization: Bearer <access_token>"
```

The discovery document is at `GET /.well-known/openid-configuration`; the authorization-code + PKCE
flow is walked end to end in the [OIDC Server cookbook](../cookbook/oidc-server.md).

## Next steps

- [gRPC Transport](grpc-transport.md) — same bearer tokens authenticate gRPC calls
- [Multi-Tenancy](multi-tenancy.md) — partition issued tokens per tenant
- [Flow](flow.md) — protect BPMN process endpoints with the same server

## Browser client profile

For a public SPA, use the [modern browser and relying-party profile](../cookbook/oidc-server.md#modern-browser-and-relying-party-evidence-profile).
It pins the code+PKCE client metadata, one exact HTTPS origin, GET/POST/OPTIONS methods,
Authorization/Content-Type/DPoP request headers, and `DPoP-Nonce` exposure. Refresh, DPoP, session
management, and RP logout have separate registrations and application responsibilities. The linked
matrix distinguishes executed server, external-RP, and managed-Chromium evidence and keeps implicit,
hybrid, `none`, password, and Relax exceptions in the historical profile.

## See also

- [Authorization](../documents/authorization.md) — server internals, flows, advisor families
- [OIDC Server cookbook](../cookbook/oidc-server.md) — the full code + PKCE walkthrough
