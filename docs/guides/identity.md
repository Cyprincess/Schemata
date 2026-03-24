# Identity

This guide adds user management to the Student CRUD app using ASP.NET Core Identity. By the end you will have registration, login, and token refresh endpoints backed by the built-in `SchemataUser` and `SchemataRole` entities.

## Add the package

```shell
dotnet add package --prerelease Schemata.Identity.Foundation
```

## Configure Identity

In `Program.cs`, add `UseIdentity()` inside the `UseSchemata` block:

```csharp
schema.UseIdentity();
```

`UseIdentity()` registers ASP.NET Core Identity with bearer-token authentication. It configures `SchemataUser` and `SchemataRole` as the default user and role types, sets up the `SchemataUserStore` and `SchemataRoleStore`, and wires a composite authentication handler that checks both bearer tokens and application cookies.

The method accepts optional callbacks for fine-grained control:

```csharp
schema.UseIdentity(
    identify: options => {
        options.AllowRegistration = true;
        options.AllowPasswordReset = true;
    },
    configure: options => {
        options.Password.RequiredLength = 8;
        options.SignIn.RequireConfirmedAccount = false;
    },
    bearer: options => {
        options.BearerTokenExpiration = TimeSpan.FromHours(1);
    }
);
```

- `identify` -- configures `SchemataIdentityOptions`, which controls which identity endpoints are enabled (registration, password reset, email change, two-factor authentication, etc.)
- `configure` -- configures ASP.NET Core `IdentityOptions` (password policy, lockout, sign-in requirements)
- `build` -- provides access to the `IdentityBuilder` for adding additional token providers or customizations
- `bearer` -- configures `BearerTokenOptions` (token expiration, refresh behavior)

## How entities work

`SchemataUser` extends `IdentityUser<long>` and implements several Schemata trait interfaces:

| Trait            | Purpose                                    |
| ---------------- | ------------------------------------------ |
| `IIdentifier`    | `long` primary key                         |
| `ICanonicalName` | Resource name (`users/{user}`)             |
| `IDisplayName`   | Human-readable display name                |
| `IConcurrency`   | Optimistic concurrency via `Timestamp`     |
| `ITimestamp`     | `CreateTime` and `UpdateTime` audit fields |

User records are stored in the `SchemataUsers` table. `SchemataRole` follows the same pattern, stored in `SchemataRoles`.

If you need custom properties on your user, subclass `SchemataUser` and pass the custom types to the generic overload:

```csharp
schema.UseIdentity<CustomUser, SchemataRole>();
```

The type constraints require `TUser : SchemataUser` and `TRole : SchemataRole`.

## Update the DbContext

Add the Identity tables to your `AppDbContext`:

```csharp
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Schemata.Identity.Skeleton.Entities;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<SchemataUser, SchemataRole, long,
        SchemataUserClaim, SchemataUserRole, SchemataUserLogin,
        SchemataRoleClaim, SchemataUserToken>(options)
{
    public DbSet<Student> Students => Set<Student>();
}
```

The base class changes from `DbContext` to `IdentityDbContext` parameterized with the Schemata identity types.

## Verify

```shell
dotnet run
```

The following identity endpoints are now available:

| Method | Path                     | Description                                     |
| ------ | ------------------------ | ----------------------------------------------- |
| `POST` | `/Authenticate/Register` | Register a new user                             |
| `POST` | `/Authenticate/Login`    | Sign in with username and password              |
| `POST` | `/Authenticate/Refresh`  | Exchange a refresh token for a new bearer token |

```shell
# Register a user
curl -X POST http://localhost:5000/Authenticate/Register \
     -H "Content-Type: application/json" \
     -d '{"email_address":"alice@example.com","password":"P@ssw0rd!"}'

# Login
curl -X POST http://localhost:5000/Authenticate/Login \
     -H "Content-Type: application/json" \
     -d '{"username":"alice@example.com","password":"P@ssw0rd!"}'

# The login response includes access_token and refresh_token.
# Use the access token to call protected endpoints:
curl http://localhost:5000/students \
     -H "Authorization: Bearer <access_token>"
```

## Next steps

- [Access Control](access-control.md) -- add role-based authorization and row-level security
- [Authorization](authorization.md) -- add an OAuth 2.0 / OpenID Connect server
- For deeper technical details, see [Identity](../documents/identity.md)
