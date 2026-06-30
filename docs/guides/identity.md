# Identity

Add user management to the Student CRUD app using ASP.NET Core Identity. By the end you'll have registration, login, and token refresh endpoints backed by `SchemataUser` and `SchemataRole`. This guide builds on [Getting Started](getting-started.md).

Schemata provides headless JSON APIs — it does not include login or registration pages.

## Add the package

`Schemata.Application.Complex.Targets` already includes `Schemata.Identity.Foundation`. If you are composing packages manually:

```shell
dotnet add package --prerelease Schemata.Identity.Foundation
```

## Enable identity

Add `UseIdentity()` inside the `UseSchemata` block in `Program.cs`:

```csharp
schema.UseIdentity();
```

`UseIdentity()` registers ASP.NET Core Identity with bearer-token authentication using `SchemataUser` and `SchemataRole` as the default types. The authentication and HTTP transport features it depends on are pulled in automatically.

All four parameters are optional:

```csharp
schema.UseIdentity(
    identify:  opts => { opts.AllowRegistration = true; },
    configure: opts => { opts.Password.RequiredLength = 8; },
    build:     ib   => { /* IdentityBuilder customizations */ },
    bearer:    opts => { opts.BearerTokenExpiration = TimeSpan.FromHours(1); }
);
```

| Parameter   | Type                              | Purpose                                                        |
| ----------- | --------------------------------- | -------------------------------------------------------------- |
| `identify`  | `Action<SchemataIdentityOptions>` | Enable/disable registration, password reset, email change, 2FA |
| `configure` | `Action<IdentityOptions>`         | Password policy, lockout, sign-in requirements                 |
| `build`     | `Action<IdentityBuilder>`         | Add token providers or custom stores                           |
| `bearer`    | `Action<BearerTokenOptions>`      | Token expiration, refresh behavior                             |

To use custom user or role types, use the generic overloads:

```csharp
schema.UseIdentity<CustomUser, SchemataRole>();
// or
schema.UseIdentity<CustomUser, CustomRole, CustomUserStore, CustomRoleStore>();
```

Type constraints: `TUser : SchemataUser, new()` and `TRole : SchemataRole`.

## Update the DbContext

Change `AppDbContext` to extend `IdentityDbContext`:

```csharp
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Schemata.Identity.Skeleton.Entities;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<SchemataUser, SchemataRole, Guid,
        SchemataUserClaim, SchemataUserRole, SchemataUserLogin,
        SchemataRoleClaim, SchemataUserToken>(options)
{
    public DbSet<Student> Students => Set<Student>();
}
```

The Identity entity set (`SchemataUser`, `SchemataRole`, the join tables) ships with class-level `[PrimaryKey(...)]` attributes already in place.

## Verify

```shell
dotnet run
```

The following endpoints are now available:

| Method | Path                     | Description                                 |
| ------ | ------------------------ | ------------------------------------------- |
| `POST` | `/Authenticate/Register` | Register a new user                         |
| `POST` | `/Authenticate/Login`    | Sign in, receive bearer token               |
| `POST` | `/Authenticate/Refresh`  | Exchange refresh token for new bearer token |

```shell
# Register (snake_case bodies via UseJsonSerializer)
curl -X POST http://localhost:5000/Authenticate/Register \
     -H "Content-Type: application/json" \
     -d '{"username":"alice","email_address":"alice@example.com","password":"P@ssw0rd!"}'

# Login (the handler looks the user up by username, not email)
curl -X POST http://localhost:5000/Authenticate/Login \
     -H "Content-Type: application/json" \
     -d '{"username":"alice","password":"P@ssw0rd!"}'

# Call a protected endpoint with the access token
curl http://localhost:5000/v1/students \
     -H "Authorization: Bearer <access_token>"
```

## Next steps

- [Access Control](access-control.md) — gate operations behind permission claims on the new users
- [Authorization](authorization.md) — issue OAuth 2.0 / OpenID Connect tokens for the same users
- [Multi-Tenancy](multi-tenancy.md) — scope users to a tenant

## See also

- [Identity](../documents/identity.md) — `SchemataUser`, `SchemataRole`, store architecture
