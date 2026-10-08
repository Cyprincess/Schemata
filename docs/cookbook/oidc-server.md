# OIDC Authorization Server

## What you'll build

A self-hosted OAuth 2.0 / OpenID Connect authorization server on
`Schemata.Authorization.Foundation`. By the end it issues authorization codes with PKCE, exchanges
them for access and ID tokens, and serves the discovery document at
`/.well-known/openid-configuration`.

The server uses the default entity types (`SchemataApplication`, `SchemataAuthorization`,
`SchemataScope`, `SchemataToken`) over EF Core. You register one scope and one public application,
then drive the authorization-code + PKCE flow with `curl`.

## Prerequisites

- Completed [Getting Started](../guides/getting-started.md) — a working Schemata project with EF
  Core.
- `Schemata.Authorization.Foundation` and `Schemata.Identity.Foundation` added. The server needs
  Identity to authenticate the resource owner.
- A signing key. The example seeds an RSA key as a security row under the issuer; production
  deployments rotate rows instead of replacing files.

```shell
dotnet add package --prerelease Schemata.Authorization.Foundation
dotnet add package --prerelease Schemata.Identity.Foundation
```

## Step 1 — Add the authorization and security tables

Add the authorization and security entities alongside the Identity sets in a regular EF Core `DbContext`:

```csharp
using Microsoft.EntityFrameworkCore;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options)
{
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
}
```

Each entity carries `[PrimaryKey(nameof(Uid))]` and its own `[Table]`; the tables are
`SchemataApplications`, `SchemataAuthorizations`, `SchemataScopes`, `SchemataSecurities`, `SchemataTokens`.

**Verify:** `dotnet ef migrations add AddAuthorization` produces a migration creating all five
tables.

## Step 2 — Register the server

```csharp
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository.Advisors;
using Schemata.Identity.Skeleton.Entities;

var builder = WebApplication.CreateBuilder(args)
    .UseSchemata(schema => {
        schema.UseLogging();
        schema.UseRouting();
        schema.UseControllers();
        schema.UseJsonSerializer();

        // Select the host's Identity user and role types.
        schema.UseIdentity<SchemataUser, SchemataRole, SchemataUserStore<SchemataUser>, SchemataRoleStore<SchemataRole>>()
            .UseRegistration().UseAccountConfirmation().UsePasswordReset().UseTwoFactorAuthentication();

        schema.UseSecurity();

        schema.UseAuthorization(o => {
                  o.Issuer         = "https://localhost:5001";
                  o.InteractionUri = "https://localhost:5001/consent"; // your consent SPA
                  o.PermitResponseType("code");
              })
              .UseAuthorizationCodeFlow()
              .UseRefreshTokenFlow()
              .UseUserInfo()
              .UseIdentity<SchemataUser>();

        schema.ConfigureServices(services => {
            services.AddRepository<SchemataApplication, EfCoreRepository<AppDbContext, SchemataApplication>>();
            services.AddRepository<SchemataAuthorization, EfCoreRepository<AppDbContext, SchemataAuthorization>>();
            services.AddRepository<SchemataScope, EfCoreRepository<AppDbContext, SchemataScope>>();
            services.AddRepository<SchemataSecurity, EfCoreRepository<AppDbContext, SchemataSecurity>>();
            services.AddRepository<SchemataToken, EfCoreRepository<AppDbContext, SchemataToken>>()
                .UseEntityFrameworkCore<AppDbContext>(
                    (_, opts) => opts.UseSqlite("Data Source=app.db"));
            services.TryAddEnumerable(
                ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataToken>, AdviceAddResourceName<SchemataToken>>());
            services.TryAddEnumerable(
                ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataAuthorization>, AdviceAddResourceName<SchemataAuthorization>>());
        });
    });
```

The framework never generates a resource `Name`. Seeded rows take an explicit `Name` (Step 3);
the token and authorization rows the server creates during a flow get one from this repository
add advisor, which `Order = 0` runs ahead of canonical-name derivation:

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
        CancellationToken    ct = default)
    {
        if (entity is ICanonicalName named && string.IsNullOrWhiteSpace(named.Name))
            named.Name = $"resource-{Guid.NewGuid():n}";

        return Task.FromResult(AdviseResult.Continue);
    }
}
```

`UseAuthorization()` installs `SchemataAuthorizationFeature` at priority 460,000,000. It depends on
`SchemataAuthenticationFeature` and `SchemataTransportHttpFeature`, pulled in automatically, and maps
the discovery and JWKS endpoints at issuer-relative paths. `UseAuthorizationCodeFlow()` adds the
authorize and token endpoints plus the PKCE,
consent, and interaction advisors; `UseRefreshTokenFlow()` adds the refresh grant; `UseUserInfo()`
adds `/Connect/Profile`. The `.UseIdentity<SchemataUser>()` on the authorization builder wires user claims into
issued tokens.

`Issuer` is required. The validation runs in `PostConfigure`, so a missing value surfaces as
`InvalidOperationException` when the options first resolve. The signing key itself is a
`SchemataSecurity` row under the issuer, seeded in Step 3; token issuance throws
`InvalidOperationException` when no valid signing row exists.

**Verify:** `dotnet run` starts, and `curl https://localhost:5001/.well-known/openid-configuration`
returns JSON with `issuer`, `authorization_endpoint`, and `token_endpoint`.

## Step 3 — Seed a scope and an application

Seed at startup through the managers. Manager methods take a `CancellationToken`; pass `default`.

```csharp
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;

using var scope = app.Services.CreateScope();
var sp = scope.ServiceProvider;

await sp.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();

var scopes = sp.GetRequiredService<IScopeManager<SchemataScope>>();
if (await scopes.FindByNameAsync("api", default) is null)
{
    await scopes.CreateAsync(new SchemataScope {
        Name        = "api",
        DisplayName = "API access",
    }, default);
}

var apps = sp.GetRequiredService<IApplicationManager<SchemataApplication>>();
if (await apps.FindByClientIdAsync("my-spa", default) is null)
{
    await apps.CreateAsync(new SchemataApplication {
        Name                    = "my-spa",
        ClientId                = "my-spa",
        ClientName              = "My SPA",
        TokenEndpointAuthMethod = "none",          // public client: no client secret
        GrantTypes              = ["authorization_code", "refresh_token"],
        ResponseTypes           = ["code"],
        Scope                   = "openid profile api",
        RedirectUris            = ["https://app.example.com/callback"],
        Permissions             = ["e:/Connect/Authorize", "e:/Connect/Token"],
    }, default);
}
```

The server signs every token with its newest valid signing row under the issuer, so seed one at
startup:

```csharp
using System.Security.Cryptography;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;

var securities = sp.GetRequiredService<ISecurityStore<SchemataSecurity>>();

var hasSigningKey = false;
await foreach (var _ in securities.ListByParentAsync(
                   "https://localhost:5001", SecurityConstants.Kinds.PrivateKey,
                   SecurityConstants.Usages.Signing, SecurityConstants.Statuses.Valid)) {
    hasSigningKey = true;
    break;
}

if (!hasSigningKey) {
    using var rsa = RSA.Create(2048);
    await securities.CreateAsync(new SchemataSecurity {
        Parent    = "https://localhost:5001",   // the issuer URI itself
        Name      = "issuer-signing",
        Kind      = SecurityConstants.Kinds.PrivateKey,
        Usage     = SecurityConstants.Usages.Signing,
        Algorithm = "RS256",
        Kid       = "signing-1",
        Value     = rsa.ExportPkcs8PrivateKeyPem(),
        Status    = SecurityConstants.Statuses.Valid,
    }, default);
}
```

Grant, response-type, and scope admission read the typed `GrantTypes`, `ResponseTypes`, and `Scope`
fields, the same metadata dynamic client registration writes. `Permissions` is administrator-only
and holds endpoint entries prefixed per `AuthorizationConstants.PermissionPrefixes.Endpoint` (`e:`).
A public client carries no secret, so PKCE is the proof of possession.

**Verify:** after seeding, the discovery document's `scopes_supported` includes `openid`, `profile`,
and `api`.

## Step 4 — Drive the authorization-code + PKCE flow

Generate a PKCE pair:

```bash
CODE_VERIFIER=$(openssl rand -base64 32 | tr '+/' '-_' | tr -d '=')
CODE_CHALLENGE=$(echo -n "$CODE_VERIFIER" \
  | openssl dgst -sha256 -binary \
  | openssl base64 | tr '+/' '-_' | tr -d '=')
```

Open the authorization endpoint in a browser:

```
https://localhost:5001/Connect/Authorize?response_type=code&client_id=my-spa\
&redirect_uri=https://app.example.com/callback\
&scope=openid+profile+api\
&code_challenge=$CODE_CHALLENGE&code_challenge_method=S256&state=xyz
```

The user signs in and consents (see the consent note below), and the server redirects to
`https://app.example.com/callback?code=<AUTH_CODE>&state=xyz`.

Exchange the code:

```bash
curl -X POST https://localhost:5001/Connect/Token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=authorization_code&client_id=my-spa\
&redirect_uri=https://app.example.com/callback\
&code=<AUTH_CODE>&code_verifier=$CODE_VERIFIER"
```

The response contains `access_token`, `id_token`, `refresh_token`, and `expires_in`.

**Verify:** decode the `access_token`. The `iss` claim equals your `Issuer`; the token carries the
`sub` from the Identity bridge.

## How consent works

The server does not render an HTML consent page. When the authorize endpoint needs the user's
decision, it issues a short-lived interaction token and redirects the browser to the SPA at
`InteractionUri`. The SPA then:

- `GET /Connect/Interact?code=<interaction>` — returns the client, the requested scopes, and the
  original request for display.
- `POST /Connect/Interact` — approves; the server records consent and continues the code flow.
- `DELETE /Connect/Interact` — denies.

You build the consent SPA; the protocol endpoints are already wired by `UseAuthorizationCodeFlow()`.

## Step 5 — Custom entity types (optional)

To add columns, subclass any of the three entities and pass them to the generic overload:

```csharp
public class MyApplication : SchemataApplication
{
    public string? CostCenter { get; set; }
}

schema.UseAuthorization<MyApplication, SchemataAuthorization, SchemataScope>(o => {
          o.Issuer = "https://localhost:5001";
          o.InteractionUri = "https://localhost:5001/consent";
      })
      .UseAuthorizationCodeFlow()
      .UseIdentity<SchemataUser>();
```

Constraints: `TApp : SchemataApplication`, `TAuth : SchemataAuthorization`,
`TScope : SchemataScope`.

## RP-initiated logout

Install `.UseEndSession()` on the authorization builder and register the RP's exact
`PostLogoutRedirectUris`. Send the issued `id_token` as `id_token_hint` to `/Connect/EndSession`
using GET query parameters or a POST form. The endpoint validates signature, issuer and client
before resolving any logout target.

The session authority proves the canonical subject, SID and RP association through installed
adapters. Live credentials plus the current trusted host ticket can permit direct logout even
after the hint expires. Host-owned recent evidence is supplied through
`IOpSessionStore.ReadLogoutAsync`; its lifetime belongs to that host's session lifecycle.
Permanent participant rows remain notification recipients and cannot prove recency.

Cookieless hints enter the configured confirmation interaction with the validated target.
Approval authenticates the captured canonical subject and authority-resolved SID, then consumes the
decision once. SID-less host tickets resolve their subject-bound browser mirror at capture and approval.
An unchanged subject-only ticket with no SID or mirror can confirm while its resolved SID remains null.
A new SID, changed mirror or repeated approval fails; configure `InteractionUri` to provide this boundary.
Successful logout clears and notifies that target before returning the registered redirect and state.
See [RP-Initiated Logout §2](https://openid.net/specs/openid-connect-rpinitiated-1_0.html#RPLogout)
and [§3](https://openid.net/specs/openid-connect-rpinitiated-1_0.html#RedirectionAfterLogout).

## Common pitfalls

**`InvalidOperationException` for `Issuer`** — validated in `PostConfigure`; set it in the
`UseAuthorization` delegate. Token issuance also throws when the issuer has no valid signing
row, when that row carries no `Algorithm` or loadable material, or when a multi-key set carries
a blank `Kid`. Seed the signing row (Step 3) before issuing tokens.

**`UseAuthorizationCodeFlow()` requires an absolute `InteractionUri`** — `AuthorizationCodeFlowFeature` checks it
once at startup and throws `InvalidOperationException` for a blank value or one that is relative.
`https://localhost:5001/consent` passes; `/consent` does not.

**Select the host user type explicitly**: `.UseIdentity<TUser>()` binds the bridge to the existing
Identity installation. Validator registration order does not select the user type.

**PKCE is on by default** — `CodeFlowOptions.RequirePkce` (and `RequirePkceS256`) default to `true`,
so a public client sends `code_challenge` with `code_challenge_method=S256`. [RFC 7636 §4.2](https://datatracker.ietf.org/doc/html/rfc7636#section-4.2)
defines the S256 transformation, and [RFC 7636 §4.4.1](https://datatracker.ietf.org/doc/html/rfc7636#section-4.4.1)
defines the authorization-endpoint response when a server requires PKCE. [RFC 9700 §2.1.1](https://datatracker.ietf.org/doc/html/rfc9700#section-2.1.1)
sets the current requirement: public clients MUST use PKCE and authorization servers MUST support
it. Relax per deployment with `UseAuthorizationCodeFlow(o => o.RelaxPkce())`; production deployments should
assess that exception against their threat model.

**The bridge is opt-in** — without `.UseIdentity<SchemataUser>()` on the authorization builder, tokens carry only
base claims; `sub`, `email`, and `role` come from the bridge.

**Token cleanup** — the core feature schedules `TokenCleanupJob<TToken>` hourly through the
Scheduling job model (`CronSchedule("0 * * * *")`). It needs `SchemataSchedulingFeature` and a
registered `TToken` repository, and calls `ITokenStore<TToken>.PruneAsync`; the store owns its
clock.

## See also

- [Authorization guide](../guides/authorization.md) — a minimal client-credentials smoke test
- [Authorization document](../documents/authorization.md) — feature internals and advisor families
- [Identity guide](../guides/identity.md) — `UseIdentity` setup
