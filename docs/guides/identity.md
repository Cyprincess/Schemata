# Identity

Add user management to the Student CRUD app using ASP.NET Core Identity. By the end you'll have registration, login, and token refresh endpoints backed by `SchemataUser` and `SchemataRole`. This guide builds on [Getting Started](getting-started.md).

Schemata provides headless JSON APIs — it does not include login or registration pages.

Examples are source-traced fragments for the prerequisite web project, not an executed sample.
Retain its implicit `System`, threading, task, ASP.NET Core, and DI imports.

## Add the package

`Schemata.Application.Complex.Targets` already includes `Schemata.Identity.Foundation`. If you are composing packages manually:

```shell
dotnet add package --prerelease Schemata.Identity.Foundation
```

## Enable identity

Add `UseIdentity()` inside the `UseSchemata` block in `Program.cs`:

```csharp
schema.UseIdentity().UseRegistration().UseAccountConfirmation().UsePasswordReset();
```

`UseIdentity()` registers ASP.NET Core Identity with bearer-token authentication using `SchemataUser` and `SchemataRole` as the default types. The authentication and HTTP transport features it depends on are pulled in automatically.

Use `SchemataUserManager<TUser>` and the registered Identity stores for native user and role administration. Use SCIM for external standards-based provisioning; the IdentityCore endpoints continue to serve registration, sign-in, account, and token operations.

All four parameters are optional:

```csharp
schema.UseIdentity(
    identify:  opts => { opts.LoginUri = "/login"; },
    configure: opts => { opts.Password.RequiredLength = 8; },
    build:     ib   => { /* IdentityBuilder customizations */ },
    bearer:    opts => { opts.BearerTokenExpiration = TimeSpan.FromHours(1); }
).UseRegistration().UseAccountConfirmation().UsePasswordReset()
 .UsePasswordChange().UseEmailChange().UsePhoneNumberChange().UseTwoFactorAuthentication();
```

| Parameter   | Type                              | Purpose                                                        |
| ----------- | --------------------------------- | -------------------------------------------------------------- |
| `identify` | `Action<SchemataIdentityOptions>` | Set the login redirect URI; operation installation uses the returned builder |
| `configure` | `Action<IdentityOptions>`         | Password policy, lockout, sign-in requirements                 |
| `build`     | `Action<IdentityBuilder>`         | Add token providers or custom stores                           |
| `bearer`    | `Action<BearerTokenOptions>`      | Token expiration, refresh behavior                             |

To use custom user or role types, use the generic overloads:

```csharp
schema.UseIdentity<CustomUser, SchemataRole>();
// custom user, role, and the five auxiliary entity types, default stores
schema.UseIdentity<CustomUser, CustomRole, CustomUserClaim, CustomRoleClaim,
    CustomUserLogin, CustomUserToken, CustomUserRole>();
// custom stores on the built-in auxiliary entities
schema.UseIdentity<CustomUser, CustomRole, CustomUserStore, CustomRoleStore>();
```

Type constraints: `TUser : SchemataUser, new()`, `TRole : SchemataRole, new()`, each auxiliary type
derives from its built-in `Schemata*` base with `new()`, and each store implements
`IUserStore<TUser>`/`IRoleStore<TRole>`. A custom store that persists its own relation entities
registers `IResourceMutation<TUser>` and `IResourceMutation<TRole>` explicitly; that registration
owns the delete cascade.

## Browser sign-in redirects

A browser that hits an `[Authorize]` endpoint without a cookie session gets HTTP 401 by default.
Set `LoginUri` to redirect it to your sign-in page instead:

```csharp
schema.UseIdentity(identify: opts => opts.LoginUri = "/login");
```

The framework appends a `continue` parameter holding the original local path, protected by ASP.NET
Data Protection. After your sign-in page authenticates the user, send the browser to
`GET ~/Authenticate/Continue?continue=<value>`; that endpoint unprotects the value, checks it is a
local URL, and redirects back to where the user started.

## Update the DbContext

Use a regular EF Core `DbContext` with the Schemata identity entity sets:

```csharp
using Microsoft.EntityFrameworkCore;
using Schemata.Identity.Skeleton.Entities;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options)
{
    public DbSet<Student> Students => Set<Student>();
    public DbSet<SchemataUser> Users => Set<SchemataUser>();
    public DbSet<SchemataRole> Roles => Set<SchemataRole>();
    public DbSet<SchemataUserClaim> UserClaims => Set<SchemataUserClaim>();
    public DbSet<SchemataRoleClaim> RoleClaims => Set<SchemataRoleClaim>();
    public DbSet<SchemataUserRole> UserRoles => Set<SchemataUserRole>();
    public DbSet<SchemataUserLogin> UserLogins => Set<SchemataUserLogin>();
    public DbSet<SchemataUserToken> UserTokens => Set<SchemataUserToken>();
}
```

The entities declare their primary keys. Users and roles keep `Guid Uid` system identities; relationship `UserId` and `RoleId` columns store canonical resource names. ASP.NET Core's generic `IdentityDbContext` requires one shared key type for principals and relationships and is not the model used by Schemata's repository stores.

## Register the identity repositories

`UseIdentity()` registers the user and role stores and their mutation owners, but it does not
register the repositories they resolve. `SchemataUserStore` takes `IRepository<SchemataUserClaim>`,
`IRepository<SchemataUserRole>`, `IRepository<SchemataUserLogin>`, and `IRepository<SchemataUserToken>`
directly, the user mutation owner resolves `IRepository<SchemataUser>`, and the role store resolves
`IRepository<SchemataRole>`, `IRepository<SchemataRoleClaim>`, and `IRepository<SchemataUserRole>`.
Register one repository per entity set from [Getting Started](getting-started.md):

```csharp
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Identity.Skeleton.Entities;

schema.ConfigureServices(services => {
    services.AddRepository<SchemataUser,          EfCoreRepository<AppDbContext, SchemataUser>>();
    services.AddRepository<SchemataRole,          EfCoreRepository<AppDbContext, SchemataRole>>();
    services.AddRepository<SchemataUserClaim,     EfCoreRepository<AppDbContext, SchemataUserClaim>>();
    services.AddRepository<SchemataRoleClaim,     EfCoreRepository<AppDbContext, SchemataRoleClaim>>();
    services.AddRepository<SchemataUserRole,      EfCoreRepository<AppDbContext, SchemataUserRole>>();
    services.AddRepository<SchemataUserLogin,     EfCoreRepository<AppDbContext, SchemataUserLogin>>();
    services.AddRepository<SchemataUserToken,     EfCoreRepository<AppDbContext, SchemataUserToken>>();
});
```

`SchemataUser` and `SchemataRole` are resources: their `Name` values come from a consumer-registered
repository create advisor, as with any other resource. User create and update begin a serializable
transaction before checking the final `NormalizedUserName`, `NormalizedEmail`, and canonical
`PhoneNumber`; updates exclude the current UID. Invalid contact phone input returns an
`INVALID_PHONE_NUMBER` field violation before staging. The mutation joins that transaction and commits once. Providers without the
requested transaction capability fail before the write.

The database created by [Getting Started](getting-started.md) through `EnsureCreatedAsync()` holds
only the `Student` set, and adding entity sets to `AppDbContext` does not change an existing
database. Schemata ships no migration tool. For development, delete `app.db` and let
`EnsureCreatedAsync()` recreate the full model, or adopt EF Core migrations for the whole model;
in production the schema is yours to evolve.

## Use email and phone aliases

`UserNameAliasMode.Disabled` is the default: registration and login select `username`, while registration can still carry email and phone contact values. Login rejects alternate key fields and `username_type`. Select another mode on the Identity builder:

```csharp
schema.UseIdentity().UseUserNameAliases(UserNameAliasMode.Enabled);
```

`Enabled` prefers `username`. Without it, registration and login require exactly one of `email_address` or `phone_number`. `Explicit` requires `username_type` with value `user-name`, `email`, or `phone-number`; the selected field supplies the key and the other contact fields may remain present. Registration assigns the selected value to `UserName`.

A phone can be a complete value such as `+14155550123`, or `phone_region` (`+1`) plus a national `phone_number` (`4155550123`). The split fields are assembled before the manager call. Supplying both forms is ambiguous. `E164PhoneNumberNormalizer` accepts a leading `+`, a nonzero first digit, then digits only, up to 15 digits; persistence uses the digits without `+`. A phone alias also assigns those canonical digits to `UserName`.

`SchemataUserManager` normalizes the final entity after registration advisors modify it. `ILookupNormalizer` handles only `UserName`, `IEmailNormalizer` handles only `Email`/`NormalizedEmail`, and `IPhoneNumberNormalizer` handles only `PhoneNumber`. Replace either contact normalizer through DI when the application needs different behavior. Raw `FindByNameAsync`, `FindByEmailAsync`, and `FindByPhoneAsync` calls normalize inside the manager.

Registration validates `RegisterRequest` with `Operations.Create`, and login validates `LoginRequest` with `Operations.Authenticate`, through `IValidationAdvisor<TRequest>` before alias resolution. Alias selection changes only the lookup key. Password checks, administrative admission, lockout, two-factor, and the `RequireConfirmed*` options keep their existing meanings. Registration leaves email and phone confirmation flags unset and uses the configured senders when confirmation codes are required.

## Provision users through SCIM

Call `UseScim()` on the Identity builder to register User and Group provisioning for the selected entity types:

```csharp
schema.UseIdentity<CustomUser, CustomRole>().UseScim();
```

Register `IRepository<ScimProvisioningBinding>` and add `ScimProvisioningBinding` to the same `DbContext` as the Identity entities. Supply an `IScimProvisioningDomainProvider` to derive the provisioning domain from the authenticated caller. The binding's external id is unique within that domain; another domain can use the same value independently. A user bound in one domain rejects PUT, PATCH, and DELETE from another domain.

SCIM maps `schemas`, `id`, `externalId`, `meta`, `userName`, `displayName`, `active`, and read-only `groups`. The entity `Uid` is the SCIM `id` and remains stable when the username changes. `active` controls `AdministrativelyActive` and rotates the security stamp when changed; temporary lockout remains independent. Provisioning does not issue cookies or tokens, confirm email addresses, or accept passwords.

Usernames use RFC 8265 `UsernameCaseMapped`. The registered `ILookupNormalizer` applies width mapping, lowercase mapping, NFC, contextual rules, and the Bidi Rule from the checked-in Unicode 6.3 tables. `UserName` retains its original form, while `NormalizedUserName` carries the comparison key used by duplicate checks, raw `FindByNameAsync` lookups, and SCIM filters. Run `dotnet eng/precis.generate.cs` to regenerate the tables from the pinned IANA and Unicode sources.

For direct administrative writes, `SchemataUserManager<TUser>` accepts an optional outer `IUnitOfWork` for create, update, delete, and `SetAdministrativeActiveAsync`. The manager stages through `IUserProvisioningStore<TUser>` and leaves an outer commit to the caller. Passing `null` lets the manager begin and commit its own serializable transaction. Custom stores implement the capability to support these methods; an incapable store fails before staging.

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

# Login (the default Disabled mode uses username)
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
