# Identity

`Schemata.Identity.Foundation` wraps ASP.NET Core Identity with Schemata entity types, a user manager extension, and a headless `AuthenticateController` that exposes registration, login, token refresh, profile management, password reset, account confirmation, and two-factor authentication as JSON API endpoints. The feature runs at priority `Orders.Extension + 30_000_000` = 430,000,000 and depends on `SchemataAuthenticationFeature` and `SchemataTransportHttpFeature`. The controller is exposed via `SchemataExtensionPart` so MVC discovers it even though it lives in a Schemata assembly.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Identity.Skeleton` | `Entities/SchemataUser.cs`, `Entities/SchemataRole.cs`, `Stores/`, `Managers/SchemataUserManager.cs` |
| `Schemata.Identity.Foundation` | `Extensions/SchemataBuilderExtensions.cs` — three `UseIdentity` overloads |
| `Schemata.Identity.Foundation` | `Features/SchemataIdentityFeature.cs` — priority 430,000,000 |
| `Schemata.Identity.Foundation` | `Controllers/AuthenticateController.cs` |

## Mechanism walkthrough

### 1. Enable the feature

Three overloads are available, each delegating to the next:

```csharp
// Default: SchemataUser + SchemataRole + default stores
builder.UseSchemata(schema => schema.UseIdentity());

// Custom user and role types, default stores
builder.UseSchemata(schema => schema.UseIdentity<MyUser, MyRole>());

// Fully custom: user, role, user store, role store
builder.UseSchemata(schema =>
    schema.UseIdentity<MyUser, MyRole, MyUserStore, MyRoleStore>());
```

All overloads accept four optional configuration delegates:

| Parameter | Type | Purpose |
| --- | --- | --- |
| `identify` | `Action<SchemataIdentityOptions>?` | Toggle which endpoints are enabled |
| `configure` | `Action<IdentityOptions>?` | Standard ASP.NET Core Identity options |
| `build` | `Action<IdentityBuilder>?` | Add token providers, custom validators, etc. |
| `bearer` | `Action<BearerTokenOptions>?` | Bearer token lifetime and validation |

### 2. What the feature registers

`SchemataIdentityFeature<TUser, TRole, TUserStore, TRoleStore>` (priority 430,000,000):

- Adds `ClaimStoreJsonConverter` to all three JSON option surfaces (`JsonSerializerOptions`, `Http.Json.JsonOptions`, `Mvc.JsonOptions`).
- Creates a `SchemataExtensionPart` and registers `AuthenticateController<TUser>` via a custom `IdentityControllerFeatureProvider`, making the controller visible to MVC without adding the entire assembly as an `ApplicationPart`.
- Registers `IdentityHandler<TUser>` (scoped) and the identity request advisor chain.
- Registers `IMailSender<T>` and `IMessageSender<T>` with no-op defaults.
- Registers `IUserStore<TUser>` and `IRoleStore<TRole>` as scoped services.
- Calls `services.AddIdentityApiEndpoints<TUser>()` with the `configure` delegate, then `.AddRoles<TRole>().AddUserManager<SchemataUserManager<TUser>>()`.
- Applies the `build` delegate to the resulting `IdentityBuilder`.
- Overrides `IdentityOptions.ClaimsIdentity` to use OIDC-standard claim types (`sub`, `preferred_username`, `email`, `role`).

### 3. Entity types

`SchemataUser` implements `IIdentifier`, `ICanonicalName`, `IDescriptive`, `IConcurrency`, and `ITimestamp`. It extends `IdentityUser<Guid>` and uses `Guid Uid` as the primary key (bridged to `IdentityUser<Guid>.Id` via `[NotMapped] override Guid Id { get => Uid; set => Uid = value; }`). Canonical name pattern: `users/{user}`.

`SchemataRole` follows the same pattern, extending `IdentityRole<Guid>`. Canonical name pattern: `roles/{role}`.

### 4. AuthenticateController

Mounted at `~/Authenticate`. All endpoints are `[ApiController]` JSON APIs — no UI is provided. The controller delegates to `IdentityHandler<TUser>`, which runs the advisor pipeline before each operation.

Key endpoints:

| Method | Path | Purpose |
| --- | --- | --- |
| `POST` | `~/Authenticate/Register` | Create account |
| `POST` | `~/Authenticate/Login` | Password login, returns bearer token |
| `POST` | `~/Authenticate/Refresh` | Exchange refresh token |
| `GET` | `~/Account/Profile` | Return claims (requires auth) |
| `PUT` | `~/Account/Profile/Email` | Request email change |
| `PUT` | `~/Account/Profile/Password` | Change password |
| `POST` | `~/Authenticate/Forgot` | Send password reset code |
| `POST` | `~/Authenticate/Reset` | Apply reset code |
| `GET` | `~/Authenticate/Confirm` | Confirm email/phone |
| `GET/POST` | `~/Authenticate/Authenticator` | 2FA status / enroll |
| `PATCH` | `~/Authenticate/Authenticator` | Disable 2FA |

## Extension points

| Interface | Purpose |
| --- | --- |
| `IIdentityRequestAdvisor<T>` | Gate or transform any identity request before the handler runs. Register via `services.TryAddEnumerable`. |
| `IMailSender<T>` | Send confirmation and reset emails. Replace the no-op default. |
| `IMessageSender<T>` | Send SMS confirmation codes. Replace the no-op default. |
| `SchemataUserManager<TUser>` | Extend with domain-specific user operations. |

## Design motivation

Using `SchemataExtensionPart` rather than a plain `ApplicationPart` keeps the controller opt-in: `SchemataControllersFeature` strips all `Schemata.*` assembly parts from MVC by default, so only controllers explicitly registered via `SchemataExtensionPart` are exposed. This prevents accidental controller discovery when a Schemata package is referenced but the feature is not enabled.

The three-overload chain (`UseIdentity()` → `UseIdentity<TUser, TRole>()` → `UseIdentity<TUser, TRole, TUserStore, TRoleStore>()`) lets you customize incrementally without repeating boilerplate.

## Caveats

- `SchemataIdentityFeature` has `Priority = Orders.Extension + 30_000_000 = 430_000_000`. See [Built-in Features](core/built-in-features.md) for the full priority table.
- The feature depends on `SchemataAuthenticationFeature` and `SchemataTransportHttpFeature` via `[DependsOn<T>]`. Both are pulled in automatically if not already registered.
- `SchemataUser` uses `Guid Uid` as the primary key, not `long`. The `IdentityUser<Guid>.Id` property is bridged via `[NotMapped]` override. Do not add a separate `long Id` column.
- `ClaimStoreJsonConverter` is registered on all three JSON option surfaces. If you configure `JsonSerializerOptions` after `UseIdentity`, ensure the converter is not removed.
- The `build` delegate receives the `IdentityBuilder` returned by `AddIdentityApiEndpoints`. Calling `builder.AddDefaultTokenProviders()` inside it is safe and idempotent.

## See also

- [Built-in Features](core/built-in-features.md) — feature priority table
- [Security](security.md) — access providers and row-level filtering
- [Authorization](authorization.md) — OAuth 2.0 / OIDC server built on top of Identity
- [Entity Traits](entity/traits.md) — `IIdentifier`, `ICanonicalName`, `IDescriptive`, `IConcurrency`, `ITimestamp`
