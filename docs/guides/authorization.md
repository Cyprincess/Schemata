# Authorization

This guide adds an OAuth 2.0 / OpenID Connect authorization server to the Student CRUD app. Building on the Identity and Access Control guides, you will configure token endpoints powered by OpenIddict so that external clients can obtain access tokens through standard OAuth 2.0 flows.

## Add the package

```shell
dotnet add package --prerelease Schemata.Authorization.Foundation
```

## Configure Authorization

In `Program.cs`, add `UseAuthorization()` inside the `UseSchemata` block and chain the flows you need:

```csharp
schema.UseAuthorization()
      .UseCodeFlow()
      .UseClientCredentialsFlow()
      .UseRefreshTokenFlow();
```

`UseAuthorization()` sets up OpenIddict with the Schemata entity stores (`SchemataApplication`, `SchemataAuthorization`, `SchemataScope`, `SchemataToken`). It returns a `SchemataAuthorizationBuilder` that provides fluent methods for enabling individual OAuth 2.0 flows:

| Method                       | Flow                         | Endpoint                                      |
| ---------------------------- | ---------------------------- | --------------------------------------------- |
| `UseCodeFlow()`              | Authorization Code with PKCE | `/Connect/Authorize` + `/Connect/Token`       |
| `UseClientCredentialsFlow()` | Client Credentials           | `/Connect/Token`                              |
| `UseRefreshTokenFlow()`      | Refresh Token                | `/Connect/Token`                              |
| `UseDeviceFlow()`            | Device Authorization         | `/Connect/Token`                              |
| `UseIntrospection()`         | Token Introspection          | Introspection endpoint                        |
| `UseRevocation()`            | Token Revocation             | Revocation endpoint                           |
| `UseEndSession()`            | OpenID Connect Logout        | End-session endpoint                          |
| `UseCaching()`               | Request Caching              | Caches authorization and end-session requests |

The `UseAuthorization()` method also accepts optional callbacks for advanced configuration:

```csharp
schema.UseAuthorization(
    serve: server => {
        server.AddEphemeralEncryptionKey()
              .AddEphemeralSigningKey();
    },
    integrate: asp => {
        asp.DisableTransportSecurityRequirement();
    }
);
```

- `serve` -- configures the `OpenIddictServerBuilder` (encryption keys, signing keys, token lifetimes)
- `integrate` -- configures the `OpenIddictServerAspNetCoreBuilder` (ASP.NET Core integration options)
- `store` -- configures the `OpenIddictCoreBuilder` (entity store options)

When `UseHttps()` has not been called on the Schemata builder, the transport security requirement is automatically disabled.

## Update the DbContext

Add the authorization tables alongside the Identity tables:

```csharp
using Schemata.Authorization.Skeleton.Entities;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<SchemataUser, SchemataRole, long,
        SchemataUserClaim, SchemataUserRole, SchemataUserLogin,
        SchemataRoleClaim, SchemataUserToken>(options)
{
    public DbSet<Student> Students => Set<Student>();

    public DbSet<SchemataApplication>   Applications  => Set<SchemataApplication>();
    public DbSet<SchemataAuthorization> Authorizations => Set<SchemataAuthorization>();
    public DbSet<SchemataScope>         Scopes         => Set<SchemataScope>();
    public DbSet<SchemataToken>         Tokens         => Set<SchemataToken>();
}
```

## Register a client application

Seed a client application so that external consumers can request tokens. For example, using `IOpenIddictApplicationManager` in a startup seeder:

```csharp
using OpenIddict.Abstractions;

// In a seeding method:
var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

if (await manager.FindByClientIdAsync("student-app") is null)
{
    await manager.CreateAsync(new OpenIddictApplicationDescriptor {
        ClientId     = "student-app",
        ClientSecret = "secret",
        ClientType   = OpenIddictConstants.ClientTypes.Confidential,
        ConsentType  = OpenIddictConstants.ConsentTypes.Implicit,
        DisplayName  = "Student App",
        Permissions  = {
            OpenIddictConstants.Permissions.Endpoints.Authorization,
            OpenIddictConstants.Permissions.Endpoints.Token,
            OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
            OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
            OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
            OpenIddictConstants.Permissions.ResponseTypes.Code,
            OpenIddictConstants.Permissions.Scopes.Email,
            OpenIddictConstants.Permissions.Scopes.Profile,
            OpenIddictConstants.Permissions.Scopes.Roles,
        },
        RedirectUris = { new Uri("http://localhost:5001/callback") },
    });
}
```

## Verify

```shell
dotnet run
```

The authorization server endpoints are now available:

| Method | Path                 | Description                        |
| ------ | -------------------- | ---------------------------------- |
| `GET`  | `/Connect/Authorize` | Authorization endpoint (code flow) |
| `POST` | `/Connect/Authorize` | Accept consent (code flow)         |
| `POST` | `/Connect/Token`     | Token exchange endpoint            |

Test the client credentials flow:

```shell
# Request an access token using client credentials
curl -X POST http://localhost:5000/Connect/Token \
     -d "grant_type=client_credentials" \
     -d "client_id=student-app" \
     -d "client_secret=secret"
```

The response contains an `access_token` that can be used to call the Student API:

```shell
curl http://localhost:5000/students \
     -H "Authorization: Bearer <access_token>"
```

For the authorization code flow, redirect the user agent to `/Connect/Authorize` with the standard OAuth 2.0 parameters (`response_type=code`, `client_id`, `redirect_uri`, `code_challenge`, `code_challenge_method`), then exchange the resulting code at `/Connect/Token`.

## Next steps

- [gRPC Transport](grpc-transport.md) -- add gRPC endpoints alongside HTTP
- For deeper technical details, see [Authorization](../documents/authorization.md)
