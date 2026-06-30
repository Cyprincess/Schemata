# Identity

`Schemata.Identity.Foundation` wraps ASP.NET Core Identity in Schemata entity types and a headless
`AuthenticateController`. The controller exposes registration, login, token refresh, profile
management, email and phone change, password reset, account confirmation, and TOTP two-factor
enrollment as JSON APIs — there are no HTML pages. The feature runs at priority 430,000,000 and
depends on `SchemataAuthenticationFeature` and `SchemataTransportHttpFeature`.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Identity.Skeleton` | `Entities/SchemataUser.cs`, `Entities/SchemataRole.cs`, the join entities, `Managers/SchemataUserManager.cs`, `Stores/`, `Advisors/`, `Json/ClaimStoreJsonConverter.cs`, `Services/IMailSender.cs`, `Services/IMessageSender.cs` |
| `Schemata.Identity.Foundation` | `Extensions/SchemataBuilderExtensions.cs` (three `UseIdentity` overloads), `Features/SchemataIdentityFeature.cs`, `Controllers/AuthenticateController*.cs`, `Handlers/IdentityHandler*.cs`, `Advisors/Advice*.cs`, `SchemataIdentityOptions.cs` |

## Entity types

`Schemata.Identity.Skeleton.Entities.SchemataUser` extends `IdentityUser<Guid>` and implements
`IIdentifier`, `ICanonicalName`, `IDescriptive`, `IConcurrency`, and `ITimestamp`. The primary key
is `Guid Uid`; the inherited `Id` is `[NotMapped]` and bridges to `Uid`:

```csharp
[NotMapped]
public override Guid Id { get => Uid; set => Uid = value; }
```

`ConcurrencyStamp` is likewise `[NotMapped]` and projects the `Guid Timestamp` concurrency token.
The table is `SchemataUsers`, canonical-name pattern `users/{user}`. `SchemataRole` follows the
same shape over `IdentityRole<Guid>`: table `SchemataRoles`, pattern `roles/{role}`.

The supporting join entities — `SchemataUserClaim`, `SchemataRoleClaim`, `SchemataUserRole`,
`SchemataUserLogin`, `SchemataUserToken` — each carry their own `[Table]` and `[PrimaryKey]`
attributes, so an `IdentityDbContext` over these types needs no extra Fluent configuration.

## Enabling the feature

Three overloads chain into one another; each takes the same four optional delegates:

```csharp
schema.UseIdentity();                                          // SchemataUser, SchemataRole, default stores
schema.UseIdentity<MyUser, MyRole>();                          // custom user/role, default stores
schema.UseIdentity<MyUser, MyRole, MyUserStore, MyRoleStore>(); // fully custom
```

| Parameter | Type | Purpose |
| --- | --- | --- |
| `identify` | `Action<SchemataIdentityOptions>?` | Toggle which endpoint groups are enabled |
| `configure` | `Action<IdentityOptions>?` | Standard ASP.NET Core Identity options (password, lockout, sign-in) |
| `build` | `Action<IdentityBuilder>?` | Add token providers, validators, custom stores |
| `bearer` | `Action<BearerTokenOptions>?` | Bearer token lifetime and validation |

Type constraints: `TUser : SchemataUser, new()`, `TRole : SchemataRole`,
`TUserStore : class, IUserStore<TUser>`, `TRoleStore : class, IRoleStore<TRole>`. The default
stores are `SchemataUserStore<TUser>` and `SchemataRoleStore<TRole>`.

## What the feature registers

`SchemataIdentityFeature<TUser, TRole, TUserStore, TRoleStore>` (`Priority = Orders.Extension +
30_000_000 = 430_000_000`) does the following in `ConfigureServices`:

- Adds `ClaimStoreJsonConverter` to all three JSON option surfaces: `JsonSerializerOptions`,
  `Microsoft.AspNetCore.Http.Json.JsonOptions`, and `Microsoft.AspNetCore.Mvc.JsonOptions`.
- Calls `AddSchemataApplicationPart<...>()`, then registers `AuthenticateController<TUser>` through
  an `IdentityControllerFeatureProvider` so MVC discovers the controller without exposing the whole
  Schemata assembly as an `ApplicationPart`.
- Registers `IdentityHandler<TUser>` (scoped) and the request-advisor chain (see below).
- Registers `IMailSender<>` and `IMessageSender<>` with the `NoOpMailSender<>` /
  `NoOpMessageSender<>` defaults.
- Registers `IUserStore<TUser>` and `IRoleStore<TRole>` with the supplied store types.
- Overrides `IdentityOptions.ClaimsIdentity` to OIDC-standard claim types: `UserIdClaimType =
  "sub"`, `UserNameClaimType = "preferred_username"`, `EmailClaimType = "email"`, `RoleClaimType =
  "role"`, `SecurityStampClaimType = "security_stamp"`.
- Builds the Identity stack: `AddIdentityApiEndpoints<TUser>(configure).AddRoles<TRole>()
  .AddUserManager<SchemataUserManager<TUser>>()
  .AddClaimsPrincipalFactory<SchemataUserClaimsPrincipalFactory<TUser, TRole>>()`, then applies the
  `build` delegate to the result. The factory issues `Claims.Subject` as the user's canonical name
  (`users/{uid}`).

## AuthenticateController

The class is routed at `[Route("~/Authenticate")]`. Most actions sit under that prefix; the
profile-management actions use absolute `~/Account/...` routes. Sign-in actions are anonymous;
account actions carry `[Authorize]`.

| Method | Route | Action | Purpose |
| --- | --- | --- | --- |
| `POST` | `~/Authenticate/Register` | `Register` | Create an account and sign in |
| `POST` | `~/Authenticate/Login` | `Login` | Password login; issues a bearer token |
| `POST` | `~/Authenticate/Refresh` | `Refresh` | Exchange a refresh token |
| `POST` | `~/Authenticate/SignOut` | `SignOut` | Clear cookie and bearer sessions |
| `GET` | `~/Authenticate/Confirm` | `Confirm` | Confirm email or phone from a code |
| `POST` | `~/Authenticate/Code` | `Code` | Send an account-confirmation code |
| `POST` | `~/Authenticate/Forgot` | `Forgot` | Send a password-reset code |
| `POST` | `~/Authenticate/Reset` | `Reset` | Reset the password with a code |
| `GET` | `~/Authenticate/Authenticator` | `Authenticator` | Return 2FA enrollment state |
| `POST` | `~/Authenticate/Enroll` | `Enroll` | Enable authenticator (TOTP) sign-in |
| `PATCH` | `~/Authenticate/Downgrade` | `Downgrade` | Disable authenticator sign-in |
| `GET` | `~/Account/Profile` | `Profile` | Return the caller's profile claims |
| `PUT` | `~/Account/Profile/Email` | `Email` | Start an email-address change |
| `PUT` | `~/Account/Profile/Phone` | `Phone` | Start a phone-number change |
| `PUT` | `~/Account/Profile/Password` | `Password` | Change the password |

Each action delegates to `IdentityHandler<TUser>`, which runs the request advisor pipeline for the
operation before performing it. The request bodies are the `*Request` models in
`Schemata.Identity.Skeleton.Models`; with snake_case serialization, `RegisterRequest` posts
`username`, `email_address`, `phone_number`, `password`, and an optional `use_cookies`.

## Request advisors

Every identity operation runs `IIdentityRequestAdvisor<T>`, whose `AdviseAsync` receives the
request `T`, the `IdentityOperation` enum value, and the caller's `ClaimsPrincipal`. The feature
registers these built-ins:

| Advisor | Request | Validates |
| --- | --- | --- |
| `AdviceRequestFeature<T>` | all | The matching `SchemataIdentityOptions` flag is enabled; throws `NotFoundException` otherwise |
| `AdviceRequestConfirmValidation` | `ConfirmRequest` | A code plus at least one of email/phone is present |
| `AdviceRequestEmailValidation<TUser>` | `ProfileRequest` | New email differs from current (on `ChangeEmail`) |
| `AdviceRequestPhoneValidation<TUser>` | `ProfileRequest` | New phone differs from current |
| `AdviceRequestPasswordValidation<TUser>` | `ProfileRequest` | Old/new password fields are coherent |
| `AdviceRequestEnrollValidation<TUser>` | `AuthenticatorRequest` | A valid 2FA code is supplied for enrollment |
| `AdviceRequestDowngradeValidation` | `AuthenticatorRequest` | A valid 2FA code is supplied for downgrade |

`AdviceRequestFeature<T>` runs at `Orders.Base = 100,000,000`; the validation advisors at
110,000,000. Operation-specific advisor interfaces — `IIdentityRegisterAdvisor<TUser>`,
`IIdentityLoginAdvisor`, `IIdentityRefreshAdvisor`, `IIdentityProfileChangeAdvisor`,
`IIdentityTwoFactorAdvisor`, `IIdentityRecoveryAdvisor`, `IIdentityProfileResponseAdvisor<TUser>` — let
you hook a single phase without filtering on the operation enum.

## SchemataUserManager

`Schemata.Identity.Skeleton.Managers.SchemataUserManager<TUser>` extends `UserManager<TUser>` with
four lookups that the Schemata stores back:

- `GetDisplayNameAsync(TUser)`
- `GetUserPrincipalNameAsync(TUser)`
- `FindByCanonicalNameAsync(string canonicalName)`
- `FindByPhoneAsync(string phone)`

These rely on the custom store interfaces `IUserDisplayNameStore<TUser>`,
`IUserPrincipalNameStore<TUser>`, `IUserCanonicalNameStore<TUser>`, and `IUserPhoneStore<TUser>`,
all implemented by `SchemataUserStore`.

## SchemataIdentityOptions

Seven booleans, all defaulting to `true`, gate the endpoint groups:

| Property | Gates |
| --- | --- |
| `AllowRegistration` | `Register` |
| `AllowAccountConfirmation` | `Confirm`, `Code` |
| `AllowPasswordReset` | `Forgot`, `Reset` |
| `AllowPasswordChange` | `~/Account/Profile/Password` |
| `AllowEmailChange` | `~/Account/Profile/Email` |
| `AllowPhoneNumberChange` | `~/Account/Profile/Phone` |
| `AllowTwoFactorAuthentication` | `Authenticator`, `Enroll`, `Downgrade` |

A disabled operation returns `NotFoundException` (HTTP 404) from `AdviceRequestFeature`.

## Extension points

| Interface | Purpose |
| --- | --- |
| `IIdentityRequestAdvisor<T>` | Gate or transform any request before the handler runs. |
| `IMailSender<TUser>` | Send confirmation and reset emails. Replace `NoOpMailSender<TUser>`. |
| `IMessageSender<TUser>` | Send SMS confirmation/reset codes. Replace `NoOpMessageSender<TUser>`. |
| `SchemataUserManager<TUser>` | Subclass for domain-specific user operations. |

## Design rationale

Registering the controller through `IdentityControllerFeatureProvider` keeps it opt-in: a project
that references the package but never calls `UseIdentity()` does not expose the endpoints. The
three-overload chain lets you swap user, role, and store types incrementally without re-stating the
delegates. Pinning the claim types to OIDC names (`sub`, `preferred_username`, `email`, `role`)
means the principal issued here is already the one the authorization server consumes.

## Caveats

- The primary key is `Guid Uid`, not `long Id`. The `Id` and `ConcurrencyStamp` overrides are
  `[NotMapped]`; do not add separate columns for them.
- The profile endpoints live under `~/Account/...`, not `~/Authenticate/...`. Authorize them
  through the bearer scheme the same way as any protected route.
- `ClaimStoreJsonConverter` is added to all three JSON surfaces. Replacing
  `JsonSerializerOptions.Converters` wholesale after `UseIdentity` drops it.

## See also

- [Identity guide](../guides/identity.md) — registration, login, and refresh on the Student app
- [Authorization](authorization.md) — the OIDC server that consumes Identity subjects
- [Security](security.md) — access providers and row-level filtering
