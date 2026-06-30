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

Add `UseAuthorization()` inside the `UseSchemata` block and chain the flows you need. Set a key and
an issuer; `AddEphemeralSigningKey()` generates an in-process RSA key for development.

```csharp
schema.UseIdentity();

schema.UseAuthorization(o => {
          o.Issuer = "https://auth.example.com";
          o.AddEphemeralSigningKey();
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

`Issuer`, a signing key, and a signing algorithm are required; the host throws
`InvalidOperationException` if any is missing. The `.UseIdentity()` call on the authorization builder
is what wires user claims (`sub`, `email`, `role`, …) into issued tokens.

## Update the DbContext

Add the authorization tables alongside the Identity tables:

```csharp
using Schemata.Authorization.Skeleton.Entities;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<SchemataUser, SchemataRole, Guid, ...>(options)
{
    public DbSet<Student>               Students       => Set<Student>();
    public DbSet<SchemataApplication>   Applications   => Set<SchemataApplication>();
    public DbSet<SchemataAuthorization> Authorizations => Set<SchemataAuthorization>();
    public DbSet<SchemataScope>         Scopes         => Set<SchemataScope>();
    public DbSet<SchemataToken>         Tokens         => Set<SchemataToken>();
}
```

Each entity carries `[PrimaryKey(nameof(Uid))]` and its own `[Table]`, so no extra mapping is
needed.

## Register a client

Seed a confidential client at startup through `IApplicationManager<SchemataApplication>`. The
`Permissions` collection uses the `e:` (endpoint), `g:` (grant type), and `s:` (scope) prefixes:

```csharp
var manager = scope.ServiceProvider
    .GetRequiredService<IApplicationManager<SchemataApplication>>();

if (await manager.FindByClientIdAsync("student-app", default) is null)
{
    await manager.CreateAsync(new SchemataApplication {
        ClientId     = "student-app",
        ClientSecret = "secret",
        ClientType   = "confidential",
        DisplayName  = "Student App",
        Permissions  = {
            "e:/Connect/Token",
            "g:client_credentials",
            "g:authorization_code",
            "g:refresh_token",
            "s:openid",
            "s:profile",
        },
        RedirectUris = { "http://localhost:5001/callback" },
    }, default);
}
```

Manager methods take a `CancellationToken`; pass `default` when seeding.

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
