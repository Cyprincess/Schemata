# OIDC Authorization Server

## What you'll build

A self-hosted OAuth 2.0 / OpenID Connect authorization server using
`Schemata.Authorization.Foundation`. By the end you'll have a running server
that issues authorization codes with PKCE, exchanges them for access and ID
tokens, and serves the OIDC discovery document at
`/.well-known/openid-configuration`.

The server uses the default entity types (`SchemataApplication`,
`SchemataAuthorization`, `SchemataScope`, `SchemataToken`) backed by EF Core.
You'll register one scope and one confidential application, then walk through
the full authorization code + PKCE flow with `curl`.

## Prerequisites

- Completed [Getting Started](../guides/getting-started.md) — you need a
  working Schemata project with EF Core configured.
- `Schemata.Authorization.Foundation` package added.
- `Schemata.Identity.Foundation` package added (the authorization server
  requires an identity store for user authentication).
- A signing key (RSA or ECDSA). The example below generates one in-process;
  production deployments should load it from a key vault.

```shell
dotnet add package --prerelease Schemata.Authorization.Foundation
dotnet add package --prerelease Schemata.Identity.Foundation
```

## Step 1 — Add the authorization tables to your DbContext

`SchemataApplication`, `SchemataAuthorization`, `SchemataScope`, and
`SchemataToken` are EF Core entities. Add them to your `DbContext`:

```csharp
using Microsoft.EntityFrameworkCore;
using Schemata.Authorization.Skeleton.Entities;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<SchemataApplication>   Applications   => Set<SchemataApplication>();
    public DbSet<SchemataAuthorization> Authorizations => Set<SchemataAuthorization>();
    public DbSet<SchemataScope>         Scopes         => Set<SchemataScope>();
    public DbSet<SchemataToken>         Tokens         => Set<SchemataToken>();
}
```

Each entity implements `IIdentifier` (Guid primary key via `Uid`),
`ICanonicalName`, `ITimestamp`, and `IConcurrency`, with
`[PrimaryKey(nameof(Uid))]` declared on the class. The table names are
`SchemataApplications`, `SchemataAuthorizations`, `SchemataScopes`, and
`SchemataTokens`.

**Verify:** `dotnet ef migrations add AddAuthorization` produces a migration
that creates all four tables.

## Step 2 — Register the authorization server

In `Program.cs`, call `UseAuthorization()` on the `SchemataBuilder` and chain
the flows you need:

```csharp
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

var rsa = RSA.Create(2048);

var builder = WebApplication.CreateBuilder(args)
    .UseSchemata(schema => {
        schema.UseLogging();
        schema.UseRouting();
        schema.UseControllers();
        schema.UseJsonSerializer();

        // Identity must be registered before Authorization.
        schema.UseIdentity<AppUser, AppRole, AppDbContext, AppDbContext>();

        schema.UseAuthorization(o => {
                   o.Issuer           = "https://auth.example.com";
                   o.SigningKey       = new RsaSecurityKey(rsa);
                   o.SigningAlgorithm = SecurityAlgorithms.RsaSha256;
               })
              .UseCodeFlow()
              .UseRefreshTokenFlow()
              .UseUserInfo();

        schema.ConfigureServices(services => {
            services.AddRepository(typeof(EntityFrameworkCoreRepository<,>))
                .UseEntityFrameworkCore<AppDbContext>(
                    (_, opts) => opts.UseSqlite("Data Source=app.db"));
        });
    });
```

`UseAuthorization()` installs `SchemataAuthorizationFeature` at priority
`450_000_000`. It depends on `SchemataAuthenticationFeature`,
`SchemataTransportHttpFeature`, and `SchemataWellKnownFeature` — those are
pulled in automatically via `[DependsOn<T>]`.

`UseCodeFlow()` registers the authorization endpoint, the token endpoint, PKCE
advisors, and the consent interaction handler.
`UseRefreshTokenFlow()` adds the refresh grant handler.
`UseUserInfo()` adds the `/connect/userinfo` endpoint.

`SchemataAuthorizationOptions` requires three fields:

| Field | Purpose |
| --- | --- |
| `Issuer` | The `iss` claim in tokens and the base URL in the discovery document |
| `SigningKey` | The key used to sign JWTs |
| `SigningAlgorithm` | The algorithm identifier, e.g. `RS256` |

**Verify:** `dotnet run` starts without exceptions. `curl
https://localhost:5001/.well-known/openid-configuration` returns a JSON
document with `"issuer"`, `"authorization_endpoint"`, and
`"token_endpoint"` fields.

## Step 3 — Seed a scope and an application

The authorization server stores scopes and applications in the database. Seed
them at startup using the managers:

```csharp
using Schemata.Authorization.Foundation.Managers;
using Schemata.Authorization.Skeleton.Entities;
using static Schemata.Abstractions.SchemataConstants;

// After builder.Build():
using var scope = app.Services.CreateScope();
var sp = scope.ServiceProvider;

await sp.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();

var scopeMgr = sp.GetRequiredService<IScopeManager<SchemataScope>>();
if (await scopeMgr.FindByNameAsync("api", default) is null)
{
    await scopeMgr.CreateAsync(new SchemataScope {
        Name        = "api",
        DisplayName = "API access",
    }, default);
}

var appMgr = sp.GetRequiredService<IApplicationManager<SchemataApplication>>();
if (await appMgr.FindByClientIdAsync("my-spa", default) is null)
{
    await appMgr.CreateAsync(new SchemataApplication {
        ClientId    = "my-spa",
        ClientType  = ClientTypes.Public,
        DisplayName = "My SPA",
        RedirectUris = ["https://app.example.com/callback"],
        Permissions  = [
            "ept:authorization",
            "ept:token",
            "gt:authorization_code",
            "gt:refresh_token",
            "scp:openid",
            "scp:profile",
            "scp:api",
        ],
    }, default);
}
```

`ClientTypes.Public` means no client secret is required. The `Permissions`
collection uses the string constants from `SchemataConstants`:

| Permission string | Meaning |
| --- | --- |
| `ept:authorization` | May use the `/connect/authorize` endpoint |
| `ept:token` | May use the `/connect/token` endpoint |
| `gt:authorization_code` | May use the authorization code grant |
| `gt:refresh_token` | May use the refresh token grant |
| `scp:openid` | May request the `openid` scope |
| `scp:api` | May request the `api` scope you created above |

**Verify:** After seeding, `curl https://localhost:5001/.well-known/openid-configuration`
shows `"scopes_supported":["openid","profile","api"]`.

## Step 4 — Drive the authorization code + PKCE flow

Generate a PKCE pair and start the flow:

```bash
# Generate a code verifier (43-128 random URL-safe characters)
CODE_VERIFIER=$(openssl rand -base64 32 | tr '+/' '-_' | tr -d '=')

# Derive the code challenge: BASE64URL(SHA256(verifier))
CODE_CHALLENGE=$(echo -n "$CODE_VERIFIER" \
  | openssl dgst -sha256 -binary \
  | openssl base64 \
  | tr '+/' '-_' \
  | tr -d '=')

# Step 1: redirect the user to the authorization endpoint
echo "Open in browser:"
echo "https://localhost:5001/connect/authorize\
?response_type=code\
&client_id=my-spa\
&redirect_uri=https://app.example.com/callback\
&scope=openid+profile+api\
&code_challenge=$CODE_CHALLENGE\
&code_challenge_method=S256\
&state=xyz"
```

After the user authenticates and consents, the server redirects to
`https://app.example.com/callback?code=<AUTH_CODE>&state=xyz`.

Exchange the code for tokens:

```bash
curl -X POST https://localhost:5001/connect/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=authorization_code\
&client_id=my-spa\
&redirect_uri=https://app.example.com/callback\
&code=<AUTH_CODE>\
&code_verifier=$CODE_VERIFIER"
```

The response contains `access_token`, `id_token`, `refresh_token`, and
`expires_in`.

**Verify:** Decode the `access_token` at jwt.io. The `iss` claim matches your
`Issuer` setting. The `aud` claim contains `my-spa`.

## Step 5 — Use custom entity types (optional)

If you need extra columns on any of the four entities, subclass them:

```csharp
public class MyApplication : SchemataApplication
{
    public string? LogoUri { get; set; }
}
```

Then pass the custom type to `UseAuthorization<TApp, TAuth, TScope, TToken>()`:

```csharp
schema.UseAuthorization<MyApplication, SchemataAuthorization,
                        SchemataScope, SchemataToken>(o => { ... })
      .UseCodeFlow()
      .UseRefreshTokenFlow();
```

All four type parameters must satisfy their constraints:
`TApp : SchemataApplication`, `TAuth : SchemataAuthorization, new()`,
`TScope : SchemataScope`, `TToken : SchemataToken, new()`.

**Verify:** EF Core migration includes the `LogoUri` column in
`SchemataApplications`.

## Common pitfalls

**`InvalidOperationException: SigningKey is required`** — `SchemataAuthorizationOptions`
validates that `SigningKey`, `SigningAlgorithm`, and `Issuer` are all set at
startup. The validation runs in `PostConfigure`, so the exception appears when
the first request arrives, not at `builder.Build()`. Set all three fields in
the `configure` delegate passed to `UseAuthorization`.

**`UseIdentity` must come before `UseAuthorization`** — the authorization
feature depends on `SchemataAuthenticationFeature`, which is pulled in by
`UseIdentity`. If you call `UseAuthorization` first, the authentication
schemes won't be registered in the right order.

**PKCE is required by default** — `CodeFlowOptions.RequirePkce` defaults to
`true`. Public clients must always send `code_challenge` and
`code_challenge_method=S256`. To relax this for a specific deployment, pass a
configure delegate to `UseCodeFlow(o => o.RelaxPkce())`, but this is not
recommended for production.

**Consent screen is not included** — the authorization server handles the
protocol but delegates the consent UI to your application. You must implement
an `AuthorizeEndpoint` controller action that renders a consent form and posts
back to `/connect/authorize`. The interaction handler
(`AuthorizeInteractionHandler`) reads the posted decision from the request.

**Token cleanup** — `TokenCleanupJob<TToken>` runs hourly through `IScheduler` when `Schemata.Scheduling.Foundation` is registered (the authorization feature adds the cron entry automatically). The `TToken` repository must be registered. If tokens accumulate, confirm that path is wired and `PruneAsync` is not throwing.

## See also

- [Authorization guide](../guides/authorization.md) — minimal OIDC smoke test
- [Identity guide](../guides/identity.md) — `UseIdentity` setup
- [Authorization document](../documents/authorization.md) — feature internals
- [Security document](../documents/security.md) — `IAccessProvider` and RBAC
