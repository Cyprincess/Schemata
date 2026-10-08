# Identity

`Schemata.Identity.Foundation` wraps ASP.NET Core Identity in Schemata entity types and a headless
`AuthenticateController`. The controller exposes registration, login, token refresh, profile
management, email and phone change, password reset, account confirmation, and TOTP two-factor
enrollment as JSON APIs — there are no HTML pages. The feature runs at priority 430,000,000 and
depends on `SchemataAuthenticationFeature` and `SchemataTransportHttpFeature`.

## Where the code lives

| Package                        | Key files                                                                                                                                                                                                                                       |
| ------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Identity.Skeleton`   | `Entities/SchemataUser.cs`, `Entities/SchemataRole.cs`, the join entities, `Managers/SchemataUserManager.cs`, `Stores/`, `Advisors/`, `Json/ClaimStoreJsonConverter.cs`, `Services/IMailSender.cs`, `Services/IMessageSender.cs`                |
| `Schemata.Identity.Foundation` | `Extensions/SchemataBuilderExtensions.cs` (three `UseIdentity` overloads), `Features/SchemataIdentityFeature.cs`, `Controllers/AuthenticateController*.cs`, `Commands/`, `Queries/`, `Handlers/IdentityHandler.cs`, `Handlers/IdentityOperationHandler*.cs`, `Advisors/Advice*.cs`, `SchemataIdentityOptions.cs` |

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

Resource `Name` values are supplied by a consumer-registered repository create advisor. Database
`Uid` and canonical `users/{name}` subjects are independent identities. `FindByIdAsync` accepts
the database GUID; `GetUserAsync` resolves canonical subject claims through `FindByCanonicalNameAsync`,
including resource names that happen to be GUID-shaped.

Refresh admission rejects tickets with missing or expired `ExpiresUtc` before validating the
security stamp or rebuilding the principal. Authentication-event claims retain the original
sign-in time and methods. User and role stores translate optimistic-concurrency failures from
both mutation and commit boundaries to Identity concurrency errors.

The supporting entities `SchemataUserClaim`, `SchemataRoleClaim`, `SchemataUserRole`,
`SchemataUserLogin` and `SchemataUserToken` store canonical `users/{name}` and `roles/{name}`
references in their inherited string `UserId`/`RoleId` properties. User and role `Uid` values remain
strict internal identities; claim rows retain their own `Uid` primary keys. Register these sets in
a regular EF Core `DbContext` using the Schemata mapping conventions, rather than the platform
`IdentityDbContext` whose shared key constraint cannot represent this split.

## Resource management surface

`UseIdentity()` returns `SchemataIdentityBuilder<TUser,TRole>`, which implements `IResourceBuilder`. Identity's User and Role management resources are exposed only after an explicit transport call:

```csharp
schema.UseSecurity();
schema.UseIdentity()
      .WithAuthentication("Bearer")
      .WithAuthorization()
      .MapHttp();
```

The shared Security extensions configure the management resource pipeline. `MapHttp()` and `MapGrpc()` are concrete Identity transport extensions that activate their domain features. The existing IdentityCore API endpoints remain separate from this resource surface.

Management mutations resolve the mutation owners `IResourceMutation<TUser>` and
`IResourceMutation<TRole>` after resource policy and mapping; the configured Identity stores
delegate their writes to the same owners. Deletion therefore uses the same transactional dependent
cleanup as direct store deletion. A replacement user or role may reuse a canonical name after
deletion without inheriting retained relationships. Direct repository writes are low-level
operations and bypass these owner lifecycle rules.

## Enabling the feature

Three overloads chain into one another; each takes the same four optional delegates:

```csharp
schema.UseIdentity();                                          // SchemataUser, SchemataRole, default stores
schema.UseIdentity<MyUser, MyRole>();                          // custom user/role, default stores
schema.UseIdentity<MyUser, MyRole, MyUserStore, MyRoleStore>(); // fully custom
```

| Parameter   | Type                               | Purpose                                                             |
| ----------- | ---------------------------------- | ------------------------------------------------------------------- |
| `identify` | `Action<SchemataIdentityOptions>?` | Configure the login redirect URI |
| `configure` | `Action<IdentityOptions>?`         | Standard ASP.NET Core Identity options (password, lockout, sign-in) |
| `build`     | `Action<IdentityBuilder>?`         | Add token providers, validators, custom stores                      |
| `bearer`    | `Action<BearerTokenOptions>?`      | Bearer token lifetime and validation                                |

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
- Supplies default dispatcher aliases when none is already registered, registers the scoped
  `IdentityHandler<TUser>` facade, and closes login, refresh, and profile request handlers for `TUser`.
  `SchemataSignInManager<TUser>` owns login verification and issuance; the operation handler owns
  the remaining Identity operations.
- Registers `IMailSender<>` and `IMessageSender<>` with the `NoOpMailSender<>` /
  `NoOpMessageSender<>` defaults.
- Registers `IUserStore<TUser>` and `IRoleStore<TRole>` with the supplied store types.
- Overrides `IdentityOptions.ClaimsIdentity` to OIDC-standard claim types: `UserIdClaimType =
"sub"`, `UserNameClaimType = "preferred_username"`, `EmailClaimType = "email"`, `RoleClaimType =
"role"`, `SecurityStampClaimType = "security_stamp"`.
- Builds the Identity stack: `AddIdentityApiEndpoints<TUser>(configure).AddRoles<TRole>()
.AddUserManager<SchemataUserManager<TUser>>()
.AddSignInManager<SchemataSignInManager<TUser>>()
.AddClaimsPrincipalFactory<SchemataUserClaimsPrincipalFactory<TUser, TRole>>()`, then applies the
  `build` delegate to the result. The factory issues `IdentityClaims.Subject` as the user's canonical name
  (`users/{name}`).

## AuthenticateController

The class is routed at `[Route("~/Authenticate")]`. Most actions sit under that prefix; the
profile-management actions use absolute `~/Account/...` routes. Sign-in actions are anonymous;
account actions carry `[Authorize]`.

| Method  | Route                          | Action          | Purpose                               |
| ------- | ------------------------------ | --------------- | ------------------------------------- |
| `POST`  | `~/Authenticate/Register`      | `Register`      | Create an account and sign in         |
| `POST`  | `~/Authenticate/Login`         | `Login`         | Password login; issues a bearer token |
| `POST`  | `~/Authenticate/Refresh`       | `Refresh`       | Exchange a refresh token              |
| `POST`  | `~/Authenticate/SignOut`       | `SignOut`       | Clear cookie and bearer sessions      |
| `GET`   | `~/Authenticate/Continue`      | `Continue`      | Resume the request that triggered a sign-in redirect |
| `GET`   | `~/Authenticate/Confirm`       | `Confirm`       | Confirm email or phone from a code    |
| `POST`  | `~/Authenticate/Code`          | `Code`          | Send an account-confirmation code     |
| `POST`  | `~/Authenticate/Forgot`        | `Forgot`        | Send a password-reset code            |
| `POST`  | `~/Authenticate/Reset`         | `Reset`         | Reset the password with a code        |
| `GET`   | `~/Authenticate/Authenticator` | `Authenticator` | Return 2FA enrollment state           |
| `POST`  | `~/Authenticate/Enroll`        | `Enroll`        | Enable authenticator (TOTP) sign-in   |
| `PATCH` | `~/Authenticate/Downgrade`     | `Downgrade`     | Disable authenticator sign-in         |
| `GET`   | `~/Account/Profile`            | `Profile`       | Return the caller's profile claims    |
| `PUT`   | `~/Account/Profile/Email`      | `Email`         | Start an email-address change         |
| `PUT`   | `~/Account/Profile/Phone`      | `Phone`         | Start a phone-number change           |
| `PUT`   | `~/Account/Profile/Password`   | `Password`      | Change the password                   |

The 14 account-operation actions dispatch their command or query directly; `SignOut` and `Continue`
retain their HTTP-specific handling. `IdentityHandler<TUser>` exposes dispatcher-backed operations.
Login consumes the current HTTP authentication context and returns `IdentityResult<Unit>` after
writing credentials; its controller returns an empty result. Other operations retain their existing
payloads. HTTP request bodies remain the `*Request` models in
`Schemata.Identity.Skeleton.Models`; with snake_case serialization, `RegisterRequest` posts
`username`, `email_address`, `phone_number`, `password`, and an optional `use_cookies`.

Login uses ASP.NET Core's `PasswordSignInAsync` and, when required, its authenticator or recovery-code
sign-in operation. The platform owns lockout, pending two-factor identity, remembered-device checks,
and recovery-code consumption. Login advisors run after verification, then host observers run before
final credentials are written. A failed advisor or observer prevents final sign-in; earlier platform
effects, including recovery-code consumption, remain committed.

`use_cookies` adds an application session cookie to the bearer response. It requests neither a
persistent cookie nor a remembered-device grant. Authentication evidence reflects the factor actually
verified, including password-only evidence when an existing remembered device bypasses a supplied code.


## Request advisors

Every identity operation runs `IIdentityRequestAdvisor<T>`, whose `AdviseAsync` receives the
request `T`, the `IdentityOperation` enum value, and the caller's `ClaimsPrincipal`. The feature
registers these built-ins:

| Advisor                                  | Request                | Validates                                                                                    |
| ---------------------------------------- | ---------------------- | -------------------------------------------------------------------------------------------- |
| `AdviceRequestConfirmValidation`         | `ConfirmRequest`       | A code plus at least one of email/phone is present                                           |
| `AdviceRequestEmailValidation<TUser>`    | `ProfileRequest`       | New email differs from current (on `ChangeEmail`)                                            |
| `AdviceRequestPhoneValidation<TUser>`    | `ProfileRequest`       | New phone differs from current                                                               |
| `AdviceRequestPasswordValidation<TUser>` | `ProfileRequest`       | Old/new password fields are coherent                                                         |
| `AdviceRequestEnrollValidation<TUser>`   | `AuthenticatorRequest` | A valid 2FA code is supplied for enrollment                                                  |
| `AdviceRequestDowngradeValidation`       | `AuthenticatorRequest` | A valid 2FA code is supplied for downgrade                                                   |

Validation advisors run at 110,000,000. Operation-specific advisor interfaces — `IIdentityRegisterAdvisor<TUser>`,
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

## Sign-in redirect

A browser reaching an `[Authorize]` endpoint without a cookie session triggers the application
cookie's login challenge. Schemata replaces the stock handling:

- `LoginUri` unset — the response is `401`, so an API host never redirects.
- `LoginUri` set — the response is `302` to `{LoginUri}?continue={payload}`. The payload is the
  original request's `PathBase + Path + QueryString`, protected by ASP.NET Data Protection under the
  purpose `Schemata.Identity.Continue`, so the browser and the login page see an opaque, tamper-
  evident blob.

After authenticating, the login page sends the browser to `GET ~/Authenticate/Continue?continue=…`.
That action unprotects the payload under the same purpose and redirects to the decoded target. Two
checks guard it: a payload that fails to unprotect raises `CryptographicException`, and a decoded
target that fails `Url.IsLocalUrl` is refused. Either path throws a `ValidationException` naming the
`continue` field, which keeps a forged or replayed payload from turning the endpoint into an open
redirect.

The redirect and its resume endpoint belong to Identity alone; they run with the Authorization
package absent. The authorization server's consent/login page is a separate setting,
`SchemataAuthorizationOptions.InteractionUri` — see [Authorization](authorization.md).

## Identity capabilities

`UseIdentity()` installs login, refresh, profile, and the host Identity system. Optional operations
are installed on its returned builder through `UseRegistration()`, `UseAccountConfirmation()`,
`UsePasswordReset()`, `UsePasswordChange()`, `UseEmailChange()`, `UsePhoneNumberChange()`, and
`UseTwoFactorAuthentication()`. Each registers its actual request handlers. MVC omits actions whose
handlers are absent; a selected handler with broken dependencies fails at use.

`SchemataIdentityOptions.LoginUri` configures the [sign-in redirect](#sign-in-redirect).

## Extension points

| Interface                    | Purpose                                                                |
| ---------------------------- | ---------------------------------------------------------------------- |
| `IIdentityRequestAdvisor<T>` | Gate or transform any request before the handler runs.                 |
| `IMailSender<TUser>`         | Send confirmation and reset emails. Replace `NoOpMailSender<TUser>`.   |
| `IMessageSender<TUser>`      | Send SMS confirmation/reset codes. Replace `NoOpMessageSender<TUser>`. |
| `SchemataUserManager<TUser>` | Subclass for domain-specific user operations.                          |

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
