# Authorization

Stand up an OAuth 2.0 / OpenID Connect authorization server on the Student app and obtain an access
token through the client-credentials flow. This guide builds on [Identity](identity.md).

## Add the package

`Schemata.Application.Complex.Targets` already includes `Schemata.Authorization.Foundation`. The
Identity bridge — the `.UseIdentity()` call on the authorization builder below — lives in a
separate package:

```shell
dotnet add package --prerelease Schemata.Authorization.Identity
```

When composing manually, also add `Schemata.Authorization.Foundation` itself.

## Enable the server

Add `UseSecurity()` and `UseAuthorization()` inside the `UseSchemata` block and chain the flows
you need. The issuer identifies the server; its signing keys are security rows seeded below.

```csharp
schema.UseIdentity();

schema.UseSecurity();           // security rows, secret verification, token stores

schema.UseAuthorization(o => {
          o.Issuer = "https://auth.example.com";
      })
      .UseIdentity()                  // bridge Identity user claims into tokens
      .UseClientCredentialsFlow()
      .UseCodeFlow()
      .UseRefreshTokenFlow();
```

`UseAuthorization()` returns a `SchemataAuthorizationBuilder`. Each method below adds one flow:

| Method                       | Grant / endpoint                                                         |
| ---------------------------- | ------------------------------------------------------------------------ |
| `UseCodeFlow()`              | `authorization_code` with PKCE — `/Connect/Authorize` + `/Connect/Token` |
| `UseClientCredentialsFlow()` | `client_credentials` — `/Connect/Token`                                  |
| `UseRefreshTokenFlow()`      | `refresh_token` — `/Connect/Token`                                       |
| `UseDeviceFlow()`            | device code — `/Connect/Device`                                          |
| `UseIntrospection()`         | `/Connect/Introspect`                                                    |
| `UseRevocation()`            | `/Connect/Revoke`                                                        |
| `UseUserInfo()`              | `/Connect/Profile`                                                       |
| `UseEndSession()`            | `/Connect/EndSession`                                                    |
| `UsePairwiseSubjects()`      | pairwise subject identifiers — OIDC Core §8                              |

`Issuer` is required; the host throws `InvalidOperationException` when it is missing. Token
issuance loads the signing rows under the issuer and throws `InvalidOperationException` when
none is valid, so seed one below. The `.UseIdentity()` call on the authorization builder
is what wires user claims (`sub`, `email`, `role`, …) into issued tokens.

## Update the DbContext

Add the authorization and security tables alongside the Identity tables:

```csharp
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<SchemataUser, SchemataRole, Guid, ...>(options)
{
    public DbSet<Student>               Students       => Set<Student>();
    public DbSet<SchemataApplication>   Applications   => Set<SchemataApplication>();
    public DbSet<SchemataAuthorization> Authorizations => Set<SchemataAuthorization>();
    public DbSet<SchemataScope>         Scopes         => Set<SchemataScope>();
    public DbSet<SchemataSecurity>      Securities     => Set<SchemataSecurity>();
    public DbSet<SchemataToken>         Tokens         => Set<SchemataToken>();
```

Each entity carries `[PrimaryKey(nameof(Uid))]` and its own `[Table]`, so no extra mapping is
needed.

When you register repositories manually, register one for `SchemataSecurity` too;
`SecurityStore<TSecurity>` reads through `IRepository<TSecurity>`.

## Register a client

Seed a confidential client at startup through `IApplicationManager<SchemataApplication>`. The
`Permissions` collection uses the `e:` (endpoint), `g:` (grant type), and `s:` (scope) prefixes:

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
        ClientId     = "student-app",
        ClientType   = "confidential",
        ClientName   = "Student App",
        Permissions  = {
            "e:/Connect/Token",
            "g:client_credentials",
            "g:authorization_code",
            "g:refresh_token",
            "s:openid",
            "s:profile",
        },
        RedirectUris = { "http://localhost:5001/callback" },
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

`SchemataApplication.Name` aliases `ClientId`. Its canonical record is
`applications/student-app`, which Schemata uses as the default `aud` value and persists in the
`Application` field of issued tokens. Bearer validation uses that canonical application reference
as the expected audience.

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

## See also

- [Authorization](../documents/authorization.md) — server internals, flows, advisor families
- [OIDC Server cookbook](../cookbook/oidc-server.md) — the full code + PKCE walkthrough
