# Authorization

Add an OAuth 2.0 / OpenID Connect authorization server to the Student CRUD app. This guide builds on [Identity](identity.md) and configures token endpoints so external clients can obtain access tokens through standard flows.

## Add the package

`Schemata.Application.Complex.Targets` already includes `Schemata.Authorization.Foundation`. If you are composing packages manually:

```shell
dotnet add package --prerelease Schemata.Authorization.Foundation
```

## Enable authorization

Add `UseAuthorization()` inside the `UseSchemata` block and chain the flows you need. `SchemataAuthorizationFeature` runs at `Order = Priority = 450_000_000`:

```csharp
schema.UseAuthorization(o => {
    o.Issuer = "https://auth.example.com";
    o.AddEphemeralSigningKey();
    o.AddEphemeralEncryptionKey();
})
.UseCodeFlow()
.UseClientCredentialsFlow()
.UseRefreshTokenFlow();
```

`UseAuthorization()` returns a `SchemataAuthorizationBuilder` for chaining flows:

| Method | Flow | Endpoint |
| ------ | ---- | -------- |
| `UseCodeFlow()` | Authorization Code with PKCE | `/Connect/Authorize` + `/Connect/Token` |
| `UseClientCredentialsFlow()` | Client Credentials | `/Connect/Token` |
| `UseRefreshTokenFlow()` | Refresh Token | `/Connect/Token` |
| `UseDeviceFlow()` | Device Authorization | `/Connect/Device` + `/Connect/Token` |
| `UseIntrospection()` | Token Introspection | `/Connect/Introspect` |
| `UseRevocation()` | Token Revocation | `/Connect/Revoke` |
| `UseEndSession()` | OpenID Connect Logout | `/Connect/EndSession` |

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

The framework entities (`SchemataApplication`, `SchemataAuthorization`, `SchemataScope`, `SchemataToken`) ship with `[PrimaryKey(nameof(Uid))]` already declared on the class.

## Register a client application

Seed a client application at startup:

```csharp
var manager = scope.ServiceProvider
    .GetRequiredService<IApplicationManager<SchemataApplication>>();

if (await manager.FindByClientIdAsync("student-app") is null)
{
    await manager.CreateAsync(new SchemataApplication {
        ClientId     = "student-app",
        ClientSecret = "secret",
        ClientType   = "confidential",
        ConsentType  = "implicit",
        DisplayName  = "Student App",
        Permissions  = {
            "gt:authorization_code",
            "gt:client_credentials",
            "gt:refresh_token",
            "scp:openid",
            "scp:profile",
        },
        RedirectUris = { "http://localhost:5001/callback" },
    });
}
```

## Verify

```shell
dotnet run
```

Client Credentials flow (no user involvement):

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

Use the token to call protected endpoints:

```shell
curl http://localhost:5000/students \
     -H "Authorization: Bearer <access_token>"
```

For the Authorization Code + PKCE flow, the full interaction sequence (authorize, consent, token exchange) is documented in [Authorization](../documents/authorization.md).

## See also

- [Access Control](access-control.md) — previous in the series: role-based authorization and row-level security
- [gRPC Transport](grpc-transport.md) — next in the series: expose the `Student` resource over gRPC
- [Identity](identity.md) — user registration and login
- [Authorization](../documents/authorization.md) — OIDC server architecture, flows, advisor interfaces
