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
- A signing key. The example generates an ephemeral one; production should load a persisted key.

```shell
dotnet add package --prerelease Schemata.Authorization.Foundation
dotnet add package --prerelease Schemata.Identity.Foundation
```

## Step 1 — Add the authorization tables

Add the four entities to your `DbContext` (here on top of `IdentityDbContext`, since you need
Identity):

```csharp
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Identity.Skeleton.Entities;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<SchemataUser, SchemataRole, Guid, ...>(options)
{
    public DbSet<SchemataApplication>   Applications   => Set<SchemataApplication>();
    public DbSet<SchemataAuthorization> Authorizations => Set<SchemataAuthorization>();
    public DbSet<SchemataScope>         Scopes         => Set<SchemataScope>();
    public DbSet<SchemataToken>         Tokens         => Set<SchemataToken>();
}
```

Each entity carries `[PrimaryKey(nameof(Uid))]` and its own `[Table]`; the tables are
`SchemataApplications`, `SchemataAuthorizations`, `SchemataScopes`, `SchemataTokens`.

**Verify:** `dotnet ef migrations add AddAuthorization` produces a migration creating all four
tables.

## Step 2 — Register the server

```csharp
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository.Advisors;

var builder = WebApplication.CreateBuilder(args)
    .UseSchemata(schema => {
        schema.UseLogging();
        schema.UseRouting();
        schema.UseControllers();
        schema.UseJsonSerializer();

        // Identity authenticates the resource owner; register it before Authorization.
        schema.UseIdentity<SchemataUser, SchemataRole, SchemataUserStore<SchemataUser>, SchemataRoleStore<SchemataRole>>();

        schema.UseAuthorization(o => {
                  o.Issuer         = "https://localhost:5001";
                  o.InteractionUri = "https://localhost:5001/consent"; // your consent SPA
                  o.AddEphemeralSigningKey();
                  o.PermitResponseType("code");
              })
              .UseIdentity()           // bridge Identity claims into tokens
              .UseCodeFlow()
              .UseRefreshTokenFlow()
              .UseUserInfo();

        schema.ConfigureServices(services => {
            services.AddRepository(typeof(EfCoreRepository<,>))
                .UseEntityFrameworkCore<AppDbContext>(
                    (_, opts) => opts.UseSqlite("Data Source=app.db"));
        });
    });
```

`UseAuthorization()` installs `SchemataAuthorizationFeature` at priority 450,000,000. It depends on
`SchemataAuthenticationFeature`, `SchemataTransportHttpFeature`, and `SchemataWellKnownFeature`,
pulled in automatically. `UseCodeFlow()` adds the authorize and token endpoints plus the PKCE,
consent, and interaction advisors; `UseRefreshTokenFlow()` adds the refresh grant; `UseUserInfo()`
adds `/Connect/Profile`. The `.UseIdentity()` on the authorization builder wires user claims into
issued tokens.

`Issuer`, a signing key, and a signing algorithm are required. The validation runs in
`PostConfigure`, so a missing value surfaces as `InvalidOperationException` when the options first
resolve.

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
        ClientId     = "my-spa",
        ClientType   = "public",                 // ClientTypes.Public — no client secret
        DisplayName  = "My SPA",
        RedirectUris = { "https://app.example.com/callback" },
        Permissions  = {
            "e:/Connect/Authorize",
            "e:/Connect/Token",
            "g:authorization_code",
            "g:refresh_token",
            "s:openid",
            "s:profile",
            "s:api",
        },
    }, default);
}
```

`Permissions` uses the `SchemataConstants.PermissionPrefixes` strings: `e:` for endpoints, `g:` for
grant types, `s:` for scopes. A public client carries no secret, so PKCE is the proof of
possession.

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

You build the consent SPA; the protocol endpoints are already wired by `UseCodeFlow()`.

## Step 5 — Custom entity types (optional)

To add columns, subclass any of the four entities and pass them to the generic overload:

```csharp
public class MyApplication : SchemataApplication
{
    public string? LogoUri { get; set; }
}

schema.UseAuthorization<MyApplication, SchemataAuthorization, SchemataScope, SchemataToken>(o => { /* ... */ })
      .UseIdentity()
      .UseCodeFlow();
```

Constraints: `TApp : SchemataApplication`, `TAuth : SchemataAuthorization, new()`,
`TScope : SchemataScope`, `TToken : SchemataToken, new()`.

## Common pitfalls

**`InvalidOperationException` for `SigningKey` / `SigningAlgorithm` / `Issuer`** — all three are
required and validated in `PostConfigure`. Set them in the `UseAuthorization` delegate.

**Register Identity before Authorization** — the authorization feature builds on
`SchemataAuthenticationFeature`, which `UseIdentity` brings in. Call `UseIdentity()` first.

**PKCE is on by default** — `CodeFlowOptions.RequirePkce` (and `RequirePkceS256`) default to `true`.
A public client must send `code_challenge` and `code_challenge_method=S256`. Relax per deployment
with `UseCodeFlow(o => o.RelaxPkce())`, not recommended for production.

**The bridge is opt-in** — without `.UseIdentity()` on the authorization builder, tokens carry only
base claims; `sub`, `email`, and `role` come from the bridge.

**Token cleanup** — the core feature schedules `TokenCleanupJob<TToken>` hourly through the
Scheduling job model (`CronSchedule("0 * * * *")`). It needs `SchemataSchedulingFeature` and a
registered `TToken` repository, and calls `ITokenManager.PruneAsync`.

## See also

- [Authorization guide](../guides/authorization.md) — a minimal client-credentials smoke test
- [Authorization document](../documents/authorization.md) — feature internals and advisor families
- [Identity guide](../guides/identity.md) — `UseIdentity` setup
