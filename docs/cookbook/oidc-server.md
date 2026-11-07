# OIDC Authorization Server

## What you'll build

A self-hosted OAuth 2.0 / OpenID Connect authorization server on
`Schemata.Authorization.Foundation`. By the end it issues authorization codes with PKCE, exchanges
them for access and ID tokens, and serves the discovery document at
`/.well-known/openid-configuration`.

The server uses the default entity types (`SchemataApplication`, `SchemataAuthorization`,
`SchemataScope`, `SchemataToken`) over EF Core. You register one scope and one public application,
then drive the authorization-code + PKCE flow with `curl`.

The recipe's code fragments are source-traced against this checkout, not executed as a complete
sample. The separately recorded browser/RP runs below identify their own host and evidence.

## Prerequisites

- Completed [Getting Started](../guides/getting-started.md) — a working Schemata project with EF
  Core.
- Add `Schemata.Authorization.Foundation`, `Schemata.Identity.Foundation`, and
  `Schemata.Authorization.Identity`. Identity authenticates the resource owner; the bridge package
  provides `.UseIdentity<SchemataUser>()` on the authorization builder.
- A signing key. The example seeds an RSA key as a security row under the issuer; production
  deployments rotate rows instead of replacing files.

```shell
dotnet add package --prerelease Schemata.Authorization.Foundation
dotnet add package --prerelease Schemata.Identity.Foundation
dotnet add package --prerelease Schemata.Authorization.Identity
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
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
        schema.UseIdentity<SchemataUser, SchemataRole>()
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
using System;
using System.Threading;
using System.Threading.Tasks;
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
using Schemata.Authorization.Skeleton;
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
        IdTokenSignedResponseAlg = AuthorizationConstants.SigningAlgorithms.RsaSha256,
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
        Algorithm = SecurityConstants.Algorithms.Rsa,
        Kid       = "signing-1",
        Value     = rsa.ExportPkcs8PrivateKeyPem(),
        Status    = SecurityConstants.Statuses.Valid,
    }, default);
}
```

The stored private-key `Algorithm` selects the PEM import primitive (`rsa`), while a JOSE
algorithm selects how that key signs a token. For client metadata, use
`AuthorizationConstants.SigningAlgorithms.RsaSha256` (`RS256`) for `IdTokenSignedResponseAlg`;
import `Schemata.Authorization.Skeleton` when using that constant. Putting `RS256` on the stored
private-key row fails `SecurityKeyMaterialExtensions.ImportPrivateKey`. The row `Name` above is
the consumer-owned resource identifier; its `Uid` is a separate repository-assigned UUID.

Grant, response-type, and scope admission read the typed `GrantTypes`, `ResponseTypes`, and `Scope`
fields, the same metadata dynamic client registration writes. `Permissions` is administrator-only
and holds endpoint entries prefixed per `AuthorizationConstants.PermissionPrefixes.Endpoint` (`e:`).
A public client carries no secret, so PKCE is the proof of possession.

**Verify:** after seeding, the discovery document's `scopes_supported` includes `openid`, `profile`,
and `api`.

## Modern browser and relying-party evidence profile

The `BrowserProfile` integration host composes a public code client with PKCE, refresh, DPoP,
session management, and RP-initiated logout. The application-owned SPA origin and CORS surface are
exact:

```csharp
schema.UseCors(cors => cors.AddDefaultPolicy(policy => policy
    .WithOrigins("https://127.0.0.1:4173")
    .WithMethods("GET", "POST", "OPTIONS")
    .WithHeaders("Authorization", "Content-Type", "DPoP")
    .WithExposedHeaders("DPoP-Nonce")));

schema.UseAuthorization(o => {
          o.Issuer = "https://localhost:7043";
          o.InteractionUri = "https://localhost:7043/interact";
      })
      .UseAuthorizationCodeFlow()
      .UseRefreshTokenFlow()
      .UseSessionManagement()
      .UseEndSession()
      .UseDemonstratingProofOfPossession(o => o.RequireForAllClients());
```

DPoP, refresh, and logout are independent optional registrations. `WithExposedHeaders` makes the
nonce challenge readable to JavaScript; the application retries the token request with a new proof
containing that nonce. The test host's public client metadata is authoritative for admission:

| Field | Value |
| --- | --- |
| `TokenEndpointAuthMethod` | `none` |
| `GrantTypes` | `authorization_code`, `refresh_token` |
| `ResponseTypes` | `code` |
| `Scope` | `openid offline_access` |
| `RedirectUris` | `https://localhost/callback`, `https://127.0.0.1:4173/callback` |
| `PostLogoutRedirectUris` | `https://127.0.0.1:4173/logged-out` |
| `Permissions` | `e:/Connect/Authorize`, `e:/Connect/Token` |

The modern callback carries `code`, `state`, `iss`, and `session_state`. Access and ID tokens are
returned by the token endpoint. `form_post` carries the same code-only response in an HTML form.

The historical profile is separate: `UseImplicitFlow()`, `UseHybridFlow()`, and `UseNoneFlow()` add
fragment/token-bearing or exclusive-`none` responses for existing clients. `RelaxPkce()`,
`RelaxPkceS256()`, `RelaxPkceDowngradeProtection()`, `RelaxCodeSingleUse()`, `RelaxNonce()`, and
`RelaxResponseModeSafety()` disable their named code-flow checks and require a deployment-specific
threat assessment. The password grant's historical, non-BCP status is documented below. These
capabilities are outside the modern browser profile.

### Executed external RP evidence

On 2026-11-05, `openid-client@6.8.1` on Node `v26.13.0` ran against the `BrowserProfile` host on
`net10.0`. Discovery, code+PKCE, state and nonce validation, DPoP token exchange, refresh, replayed
state rejection, replayed code rejection, and RSA-OAEP/A128CBC-HS256 ID-token decryption passed.
Two concurrent redemptions produced one credential set and one rejection. The losing response was
non-conforming to the RP library (`OAUTH_RESPONSE_IS_NOT_CONFORM`); this boundary is recorded rather
than described as a clean OAuth rejection. The sanitized outcomes and exact host command are in
[`external-rp.json`](../evidence/authorization/external-rp.json).

The package was installed only in the temporary task workspace. The run used this command with
`NODE_EXTRA_CA_CERTS` naming the exported ASP.NET Core development certificate:

```shell
node .superpowers/sdd/2026-11-03-p0-p3-remediation/security-task-14-work/external-rp.mjs
```

### Chromium evidence status

Executed on 2026-11-05 with a dedicated managed Chromium target (`app: { relay: false, tern: false }`), not the user's browser or an existing profile. The target was `A0B11ABBC2D90F1315C1597385529CF2`, running `Chrome/156.0.8078.76` in headless mode. The Authorization host used:

```bash
ASPNETCORE_ENVIRONMENT=BrowserProfile ASPNETCORE_URLS=https://localhost:7043 \
  BrowserProfile__Issuer=https://localhost:7043 \
  dotnet artifacts/bin/Schemata.Authorization.Integration.Tests/Release/net10.0/Schemata.Authorization.Integration.Tests.dll
```

The controller used `browser.open({ name: "security-scim-browser-evidence", app: { relay: false, tern: false }, ignore_https_errors: true })` and closed the tab after the run. `ignore_https_errors` was limited to the local development-certificate hosts. The exactly-once browser state snapshot is `docs/evidence/authorization/security-scim-browser-evidence.json`; its cookie value is redacted. `docs/evidence/authorization/browser-code-pkce.har` is the actual request/response capture, with codes, proofs, cookies, credentials, and response bodies omitted or redacted.

Observed scenarios:

- Code+PKCE query and `form_post` callbacks carried `code` and `state`, with no authorization-response `access_token` or `id_token`.
- The exact SPA origin passed preflight and exposed `DPoP-Nonce`; `https://localhost:4173` failed the denied-origin fetch.
- The first DPoP token request returned `use_dpop_nonce`; the retry with the returned nonce succeeded. Refresh also completed the nonce challenge and issued a new access token using the same key.
- `Connect/CheckSession` returned `unchanged` for the current `session_state` and `changed` for a stale value through iframe `postMessage`.
- An initial harness request used the unregistered `/logout-callback` and returned 200 with a JSON `null` body; that URI was rejected by the registered-client check. Authorization host logs confirmed deletion of the target session participant and clearing of the SID cookie. The corrected request used the registered `/logged-out` and completed the redirect to `https://127.0.0.1:4173/logged-out?state=registered-logout-state`. The earlier unregistered redirect is not claimed as a successful composition test.

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

To add columns, subclass any of the four entities and pass them to the generic overload:

```csharp
public class MyApplication : SchemataApplication
{
    public string? CostCenter { get; set; }
}

schema.UseAuthorization<MyApplication, SchemataAuthorization, SchemataScope, SchemataSubjectMapping>(o => {
          o.Issuer = "https://localhost:5001";
          o.InteractionUri = "https://localhost:5001/consent";
      })
      .UseAuthorizationCodeFlow()
      .UseIdentity<SchemataUser>();
```

Constraints: `TApplication : SchemataApplication, new()`, `TAuthorization : SchemataAuthorization, new()`,
`TScope : SchemataScope`, `TSubjectMapping : SchemataSubjectMapping, new()`. Call `UseSecurity()`
before `UseAuthorization()`; the token and security entity types come from that registered feature.

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

## Historical password grant

`UseResourceOwnerPasswordFlow()` adds only the RFC 6749 §4.3 `password` grant to the token endpoint
and discovery. Client `GrantTypes` must contain `password`; normal token endpoint permission and
scope admission still apply. The authorization Identity bridge supplies the validator and enforces
password verification, lockout, administrative admission, and two-factor rejection before creating
the canonical principal. Other hosts register `IResourceOwnerPasswordValidator` directly. Missing
validation or a rejected principal returns `invalid_grant`.

[RFC 9700 §2.4](https://www.rfc-editor.org/rfc/rfc9700.html#section-2.4) prohibits this grant because
it exposes user credentials to the client and cannot carry interactive multi-factor authentication.
The feature supports explicit historical migrations and is not RFC 9700 Best Current Practice
compliant. It issues access tokens and `offline_access` refresh tokens, without ID tokens or host
cookies. Keep passwords out of validator and advisor logs, sign-in properties, and resource names.

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
