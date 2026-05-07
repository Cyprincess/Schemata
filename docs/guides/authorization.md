# Authorization

This guide adds an OAuth 2.0 / OpenID Connect authorization server to the Student CRUD app. Building on the Identity and Access Control guides, you will configure token endpoints so external clients can obtain access tokens through standard OAuth 2.0 flows.

## Configuration

`Schemata.Application.Complex.Targets` already includes `Schemata.Authorization.Foundation`. If you are composing packages manually:

```shell
dotnet add package --prerelease Schemata.Authorization.Foundation
```

In `Program.cs`, add `UseAuthorization()` inside the `UseSchemata` block and chain the flows you need:

```csharp
schema.UseAuthorization()
      .UseCodeFlow()
      .UseClientCredentialsFlow()
      .UseRefreshTokenFlow();
```

`UseAuthorization()` sets up the authorization server with the Schemata entity stores (`SchemataApplication`, `SchemataAuthorization`, `SchemataScope`, `SchemataToken`). It returns a `SchemataAuthorizationBuilder` with fluent methods for enabling individual OAuth 2.0 flows:

| Method                       | Flow                         | Endpoint                                |
| ---------------------------- | ---------------------------- | --------------------------------------- |
| `UseCodeFlow()`              | Authorization Code with PKCE | `/Connect/Authorize` + `/Connect/Token` |
| `UseClientCredentialsFlow()` | Client Credentials           | `/Connect/Token`                        |
| `UseRefreshTokenFlow()`      | Refresh Token                | `/Connect/Token`                        |
| `UseDeviceFlow()`            | Device Authorization         | `/Connect/Device` + `/Connect/Token`    |
| `UseIntrospection()`         | Token Introspection          | `/Connect/Introspect`                   |
| `UseRevocation()`            | Token Revocation             | `/Connect/Revoke`                       |
| `UseEndSession()`            | OpenID Connect Logout        | `/Connect/EndSession`                   |

`UseAuthorization()` also accepts an optional `Action<SchemataAuthorizationOptions>`:

```csharp
schema.UseAuthorization(o => {
    o.Issuer = "https://auth.example.com";
    o.AddEphemeralSigningKey();
    o.AddEphemeralEncryptionKey();
});
```

`SchemataAuthorizationOptions` exposes signing/encryption keys, token lifetimes, PKCE enforcement, subject identifier type, and more. See [Authorization](../documents/authorization.md) for the full reference.

## Update the DbContext

Add the authorization tables alongside the Identity tables:

```csharp
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Identity.Skeleton.Entities;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<SchemataUser, SchemataRole, Guid,
        SchemataUserClaim, SchemataUserRole, SchemataUserLogin,
        SchemataRoleClaim, SchemataUserToken>(options)
{
    public DbSet<Student> Students => Set<Student>();

    public DbSet<SchemataApplication>   Applications  => Set<SchemataApplication>();
    public DbSet<SchemataAuthorization> Authorizations => Set<SchemataAuthorization>();
    public DbSet<SchemataScope>         Scopes         => Set<SchemataScope>();
    public DbSet<SchemataToken>         Tokens         => Set<SchemataToken>();

    protected override void ConfigureConventions(
        ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.UseTableKeyConventions();
    }
}
```

## Register a client application

Seed a client application so that external consumers can request tokens. For example, using `IApplicationManager<SchemataApplication>` in a startup seeder:

```csharp
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;

// In a seeding method:
var manager = scope.ServiceProvider.GetRequiredService<IApplicationManager<SchemataApplication>>();

if (await manager.FindByClientIdAsync("student-app") is null)
{
    var application = new SchemataApplication {
        ClientId     = "student-app",
        ClientSecret = "secret",
        ClientType   = "confidential",
        ConsentType  = "implicit",
        DisplayName  = "Student App",
        Permissions  = {
            "gt:authorization_code",
            "gt:client_credentials",
            "gt:refresh_token",
            "scp:email",
            "scp:profile",
            "scp:roles",
        },
        RedirectUris = { "http://localhost:5001/callback" },
    };

    await manager.CreateAsync(application);
}
```

## Verify

```shell
dotnet run
```

### Client Credentials flow

The simplest flow -- no user involvement. The client authenticates with its own credentials:

```shell
curl -X POST http://localhost:5000/Connect/Token \
     -d "grant_type=client_credentials" \
     -d "client_id=student-app" \
     -d "client_secret=secret"
```

Response:

```json
{
  "access_token": "eyJhbG...",
  "token_type": "Bearer",
  "expires_in": 3600
}
```

Use the token to call protected APIs:

```shell
curl http://localhost:5000/students \
     -H "Authorization: Bearer <access_token>"
```

### Authorization Code flow with PKCE

This flow involves user authentication and consent. Schemata does not provide UI -- you implement consent, device verification, and logout pages yourself (a SPA or plain JavaScript page is recommended). When consent is needed, the server redirects to a configurable URI where your page calls the JSON APIs described below.

**Step 1 -- Redirect the user to the authorization endpoint:**

```
GET /Connect/Authorize?client_id=student-app
    &redirect_uri=http://localhost:5001/callback
    &response_type=code
    &scope=openid profile
    &state=abc123
    &code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM
    &code_challenge_method=S256
```

If the application's `ConsentType` is `implicit` and the user is authenticated, the server issues an authorization code immediately and redirects to `redirect_uri`.

If consent is required, the server creates an interaction token and redirects to your interaction page:

```
302 → https://auth.example.com/interact?code=AbCdEf123456
```

**Step 2 -- Retrieve consent details:**

```shell
curl http://localhost:5000/Connect/Interact\
     ?code=AbCdEf123456\
     &code_type=urn:schemata:authorization:token-type:interaction
```

Response:

```json
{
  "type": "authorize",
  "request": {
    "client_id": "student-app",
    "redirect_uri": "http://localhost:5001/callback",
    "response_type": "code",
    "scope": "openid profile"
  },
  "application": {
    "client_id": "student-app",
    "display_name": "Student App"
  },
  "scopes": [
    { "name": "openid", "display_name": "OpenID" },
    { "name": "profile", "display_name": "Profile" }
  ],
  "iss": "http://localhost:5000"
}
```

**Step 3a -- Grant consent** (user must be authenticated):

```shell
curl -X POST http://localhost:5000/Connect/Token \
     -H "Authorization: Bearer <user_session_token>" \
     -d "grant_type=urn:ietf:params:oauth:grant-type:token-exchange" \
     -d "subject_token=AbCdEf123456" \
     -d "subject_token_type=urn:schemata:authorization:token-type:interaction"
```

The server issues an authorization code and redirects to `redirect_uri`:

```
302 → http://localhost:5001/callback?code=AuthzCode123&state=abc123&iss=http://localhost:5000
```

**Step 3b -- Deny consent** (user must be authenticated):

```shell
curl -X DELETE http://localhost:5000/Connect/Interact\
     ?code=AbCdEf123456\
     &code_type=urn:schemata:authorization:token-type:interaction
```

Returns `204 No Content`. The interaction token is revoked.

**Step 4 -- Exchange the authorization code for tokens:**

```shell
curl -X POST http://localhost:5000/Connect/Token \
     -d "grant_type=authorization_code" \
     -d "code=AuthzCode123" \
     -d "client_id=student-app" \
     -d "client_secret=secret" \
     -d "redirect_uri=http://localhost:5001/callback" \
     -d "code_verifier=dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"
```

Response:

```json
{
  "access_token": "eyJhbG...",
  "token_type": "Bearer",
  "expires_in": 3600,
  "id_token": "eyJhbG...",
  "refresh_token": "rt_AbCdEf..."
}
```

### Device Authorization flow (RFC 8628)

For devices without a browser (smart TVs, CLI tools).

**Step 1 -- Device requests authorization:**

```shell
curl -X POST http://localhost:5000/Connect/Device \
     -d "client_id=device-app" \
     -d "scope=openid profile"
```

Response:

```json
{
  "device_code": "GmRhmhcxhwAzkoEqiMEg_DnyEysNkuNh",
  "user_code": "ABCD-1234",
  "verification_uri": "https://auth.example.com/device",
  "verification_uri_complete": "https://auth.example.com/device?code=ABCD-1234",
  "expires_in": 1800,
  "interval": 5
}
```

**Step 2 -- User opens `verification_uri` in a browser and enters the user code. The page retrieves details:**

```shell
curl http://localhost:5000/Connect/Interact\
     ?code=ABCD-1234\
     &code_type=urn:schemata:authorization:token-type:user-code
```

Response:

```json
{
  "type": "device",
  "application": {
    "client_id": "device-app",
    "display_name": "Smart TV"
  },
  "scopes": [
    { "name": "openid", "display_name": "OpenID" },
    { "name": "profile", "display_name": "Profile" }
  ],
  "iss": "http://localhost:5000"
}
```

**Step 3 -- User approves** (authenticated):

```shell
curl -X POST http://localhost:5000/Connect/Token \
     -H "Authorization: Bearer <user_session_token>" \
     -d "grant_type=urn:ietf:params:oauth:grant-type:token-exchange" \
     -d "subject_token=ABCD-1234" \
     -d "subject_token_type=urn:schemata:authorization:token-type:user-code"
```

Returns `204 No Content`. The device code is marked as authorized.

**Step 4 -- Device polls for tokens** (every `interval` seconds):

```shell
curl -X POST http://localhost:5000/Connect/Token \
     -d "grant_type=urn:ietf:params:oauth:grant-type:device_code" \
     -d "device_code=GmRhmhcxhwAzkoEqiMEg_DnyEysNkuNh" \
     -d "client_id=device-app"
```

While pending, returns `400` with `{"error": "authorization_pending"}`. After the user approves, returns tokens:

```json
{
  "access_token": "eyJhbG...",
  "token_type": "Bearer",
  "expires_in": 3600,
  "id_token": "eyJhbG..."
}
```

### Refresh Token flow

```shell
curl -X POST http://localhost:5000/Connect/Token \
     -d "grant_type=refresh_token" \
     -d "refresh_token=rt_AbCdEf..." \
     -d "client_id=student-app" \
     -d "client_secret=secret"
```

The old refresh token is revoked and a new one is issued (rotation per RFC 9700 §2.2.2). Scope narrowing is supported -- pass `scope` to request a subset of the original grant.

## Customizing via advisors

The authorization module uses the [advice pipeline](../documents/core/advice-pipeline.md) for cross-cutting concerns. You can add custom advisors to modify behavior at any point in the flow.

For example, to add custom claims to all issued tokens, implement `IClaimsAdvisor`:

```csharp
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;

public sealed class CustomClaimsAdvisor : IClaimsAdvisor
{
    public int Order => 100_000_000;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext ctx, List<Claim> claims, CancellationToken ct = default
    ) {
        claims.Add(new Claim("tenant_id", "acme"));
        return Task.FromResult(AdviseResult.Continue);
    }
}
```

Register it in your startup:

```csharp
services.TryAddEnumerable(
    ServiceDescriptor.Scoped<IClaimsAdvisor, CustomClaimsAdvisor>());
```

See [Authorization](../documents/authorization.md) for the full list of advisor interfaces and the handler architecture.

## Next steps

- [gRPC Transport](grpc-transport.md) -- add gRPC endpoints alongside HTTP
- For deeper technical details, see [Authorization](../documents/authorization.md)
