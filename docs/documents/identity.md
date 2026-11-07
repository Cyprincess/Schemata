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
| `Schemata.Identity.Foundation` | `Extensions/SchemataBuilderExtensions.cs` (four `UseIdentity` overloads), `Features/SchemataIdentityFeature.cs`, `Controllers/AuthenticateController*.cs`, `Commands/`, `Queries/`, `Handlers/IdentityHandler.cs`, `Handlers/IdentityOperationHandler*.cs`, `Advisors/Advice*.cs`, `SchemataIdentityOptions.cs` |

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

These are three separate contracts with different uniqueness scopes. A canonical name is unique
among currently active instances of its resource (AIP-122: one API, one name); a soft-deleted or
permanently removed instance releases the name, and IDs of deleted resources are never reused
(AIP-164). The database `Uid` is the strict internal identity and is never reassigned. OpenID
Connect additionally requires the wire `sub` to be never reassigned within one issuer; Schemata
currently emits the canonical name as `sub`, so a consumer who deletes and reconstructs a user
under the same canonical name must treat the two instances as the same OIDC subject — or configure
`subject_type = pairwise`, whose projected identifiers are derived from the (sector, subject,
salt) tuple and persisted per application in `SchemataSubjectMappings`. Choosing a
never-reassigned wire `sub` source remains a consumer decision under this policy; the framework
does not tombstone or reserve names.

Refresh admission rejects tickets with missing or expired `ExpiresUtc` before validating the
security stamp or rebuilding the principal. Authentication-event claims retain the original
sign-in time and methods. User and role stores translate optimistic-concurrency failures from
both mutation and commit boundaries to Identity concurrency errors.

`SchemataUser.AdministrativelyActive` defaults to `true`. `SetAdministrativeActiveAsync` persists
the requested state and rotates the security stamp when the state changes. Password sign-in and
ticket renewal reject an inactive user before issuing replacement credentials. This administrative
state is independent of lockout: deactivation and reactivation preserve `LockoutEnd`.

Changing the administrative state does not synchronously revoke every existing application cookie.
Cookie authentication rechecks the rotated security stamp on the host's configured ASP.NET Core
Identity validation cadence. New password sign-ins and ticket renewals apply the administrative
check at their next admission.

The supporting entities `SchemataUserClaim`, `SchemataRoleClaim`, `SchemataUserRole`,
`SchemataUserLogin` and `SchemataUserToken` store canonical `users/{name}` and `roles/{name}`
references in their inherited string `UserId`/`RoleId` properties. User and role `Uid` values remain
strict internal identities; claim rows retain their own `Uid` primary keys. Register these sets in
a regular EF Core `DbContext` using the Schemata mapping conventions, rather than the platform
`IdentityDbContext` whose shared key constraint cannot represent this split.

## Native management and mutation ownership

Use `SchemataUserManager<TUser>` and `SchemataRoleManager<TRole>` with the registered stores for
native administration. Recommend SCIM for external User/Group provisioning. The IdentityCore API
endpoints retain registration, authentication, account, and token routes. These HTTP protocol
surfaces do not imply a native Resource management controller or gRPC service.

Sources: `src/Schemata.Identity.Skeleton/Managers/SchemataUserManager.cs`, `SchemataRoleManager.cs`,
`src/Schemata.Identity.Foundation/Features/SchemataIdentityFeature.cs`, and
`src/Schemata.Identity.Scim/Features/SchemataIdentityScimFeature.cs`.

Store and manager mutations resolve the mutation owners `IResourceMutation<TUser>` and
`IResourceMutation<TRole>`. Deletion therefore uses the same transactional dependent
cleanup as direct store deletion. A replacement user or role may reuse a canonical name after
deletion without inheriting retained relationships. Direct repository writes are low-level
operations and bypass these owner lifecycle rules.

## Enabling the feature

Four overloads chain into one another; each takes the same four optional delegates:

```csharp
schema.UseIdentity();                                  // SchemataUser, SchemataRole, default auxiliary entities and stores
schema.UseIdentity<MyUser, MyRole>();                  // custom user/role, default auxiliary entities and stores
schema.UseIdentity<MyUser, MyRole, MyUserClaim, MyRoleClaim,
    MyUserLogin, MyUserToken, MyUserRole>();           // seven custom entity types, default stores
schema.UseIdentity<MyUser, MyRole, MyUserStore, MyRoleStore>(); // fully custom stores on the built-in auxiliary entities
```

| Parameter   | Type                               | Purpose                                                             |
| ----------- | ---------------------------------- | ------------------------------------------------------------------- |
| `identify` | `Action<SchemataIdentityOptions>?` | Configure the login redirect URI |
| `configure` | `Action<IdentityOptions>?`         | Standard ASP.NET Core Identity options (password, lockout, sign-in) |
| `build`     | `Action<IdentityBuilder>?`         | Add token providers, validators, custom stores                      |
| `bearer`    | `Action<BearerTokenOptions>?`      | Bearer token lifetime and validation                                |

Type constraints: `TUser : SchemataUser, new()`, `TRole : SchemataRole, new()`, each auxiliary
entity parameter constrained to its `SchemataUserClaim`/`SchemataRoleClaim`/`SchemataUserLogin`/
`SchemataUserToken`/`SchemataUserRole` base with `new()`, `TUserStore : class, IUserStore<TUser>`,
`TRoleStore : class, IRoleStore<TRole>`. The default stores close over every selected entity type:
`SchemataUserStore<TUser, TRole, TUserClaim, TUserRole, TUserLogin, TUserToken, TRoleClaim>` and
`SchemataRoleStore<TRole, TRoleClaim, TUserRole>`. A custom store that persists its own relation
entities registers `IResourceMutation<TUser>` and `IResourceMutation<TRole>` explicitly; that
registration owns the delete cascade.

## What the feature registers

`SchemataIdentityFeature<TUser, TRole, TUserClaim, TRoleClaim, TUserLogin, TUserToken, TUserRole,
TUserStore, TRoleStore>` (`Priority = Orders.Extension + 30_000_000 = 430_000_000`) does the
following in `ConfigureServices`:

- Adds `ClaimStoreJsonConverter` to all three JSON option surfaces: `JsonSerializerOptions`,
  `Microsoft.AspNetCore.Http.Json.JsonOptions`, and `Microsoft.AspNetCore.Mvc.JsonOptions`.
- Calls `AddSchemataApplicationPart<...>()`, then registers `AuthenticateController<TUser>` through
  an `IdentityControllerFeatureProvider` so MVC discovers the controller without exposing the whole
  Schemata assembly as an `ApplicationPart`.
- Supplies default dispatcher aliases when none is already registered, registers the scoped
  `IdentityHandler<TUser>` facade, and closes login, refresh, and profile request handlers for
  `TUser`. `SchemataSignInManager<TUser>` owns login verification and issuance; the operation
  handler owns the remaining Identity operations. Email and SMS delivery are optional
  capabilities: `IMailSender<TUser>` / `IMessageSender<TUser>` resolve only on the operation
  path that needs the channel, so a host without a registered sender fails that operation at
  its use instead of reporting a fabricated delivery.
- Registers `IUserStore<TUser>` and `IRoleStore<TRole>` with the supplied store types.
- Registers the PRECIS username normalizer, uppercase `IEmailNormalizer`, E.164 `IPhoneNumberNormalizer`, the Disabled `IUserAliasResolver`, and the open-generic `IIdentityAliasUniquenessAdvisor<TUser>` pipeline.
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
`Schemata.Identity.Skeleton.Models`; with snake_case serialization, registration and login accept
`username`, `email_address`, `phone_number`, `phone_region`, `username_type`, and `password`.
Registration also accepts optional `use_cookies`; login accepts the two-factor, ACR, and cookie fields.

Login uses ASP.NET Core's `PasswordSignInAsync` and, when required, its authenticator or recovery-code
sign-in operation. The platform owns lockout, pending two-factor identity, remembered-device checks,
and recovery-code consumption. Login advisors run after verification, then host observers run before
final credentials are written. A failed advisor or observer prevents final sign-in; earlier platform
effects, including recovery-code consumption, remain committed.

`use_cookies` adds an application session cookie to the bearer response. It requests neither a
persistent cookie nor a remembered-device grant. Authentication evidence reflects the factor actually
verified, including password-only evidence when an existing remembered device bypasses a supplied code.


## Username aliases and normalization

`UseUserNameAliases(UserNameAliasMode mode)` replaces the registered `IUserAliasResolver`. The default resolver uses `Disabled` mode.

| Mode | Registration and login key selection |
| ---- | ------------------------------------ |
| `Disabled` | `username` is required. Registration retains email and phone as contacts; login rejects alternate key fields and `username_type`. |
| `Enabled` | A supplied `username` wins. Without it, exactly one of `email_address` or `phone_number` is required. |
| `Explicit` | `username_type` selects `user-name`, `email`, or `phone-number`; the selected value is required and other contact values may coexist. |

Registration assigns the selected value to `UserName`. `username_type` is rejected outside `Explicit`, and automatic email-plus-phone input is rejected as ambiguous. A complete E.164 phone and the split `phone_region` plus national `phone_number` form are also ambiguous. Split input is concatenated before manager invocation. The built-in `E164PhoneNumberNormalizer` accepts only a leading plus sign, a nonzero initial digit, and up to 15 total digits, then returns digits without the plus sign. Registration uses those digits for a phone-alias username.

Create, update, registration, phone confirmation, SCIM, and resource writes pass through `SchemataUserManager<TUser>`. After entity advisors run, the manager computes `NormalizedUserName` through `ILookupNormalizer`, `NormalizedEmail` through `IEmailNormalizer`, and canonical `PhoneNumber` through `IPhoneNumberNormalizer`. These services are independent and replaceable through DI. Raw `FindByNameAsync`, `FindByEmailAsync`, and `FindByPhoneAsync` normalize at the same manager boundary; stores select the supplied comparison key without another normalization pass.

`IIdentityAliasUniquenessAdvisor<TUser>` implementations run in ascending `Order` after final normalization and before store staging. The manager passes the explicit `IUnitOfWork` and begins a serializable transaction when the caller does not supply one. `AdviceAliasUniqueness<TUser>` joins the user repository to that transaction, checks `NormalizedUserName`, `NormalizedEmail`, and canonical `PhoneNumber`, and excludes the current UID on update. Manager-owned writes commit once. A store without `IUserProvisioningStore<TUser>` fails before staging. Invalid contact phone input produces an `INVALID_PHONE_NUMBER` field violation for `phone_number` before staging.

Login resolves only the key, then uses the existing password, `PreSignInCheck`, `CanSignInAsync`, administrative-active, lockout, and two-factor pipeline. Zero or multiple matches produce the same invalid-credential failure. Confirmation flags and `RequireConfirmedAccount`, `RequireConfirmedEmail`, and `RequireConfirmedPhoneNumber` retain their platform meanings. Registration leaves contact confirmation unset and uses the existing channel senders for required codes.

`RegisterRequest` validation uses `Operations.Create`; `LoginRequest` validation uses `Operations.Authenticate`. Both run through `Validation.Skeleton.ValidationHelper` and `IValidationAdvisor<TRequest>` before alias resolution. `Schemata.Identity.Foundation` references `Schemata.Validation.Skeleton` directly and has no FluentValidation dependency.

## Request advisors

Every identity operation runs `IIdentityRequestAdvisor<TRequest,TResponse>`, whose `AdviseAsync` receives the
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
`UseTwoFactorAuthentication()`. Each registers its actual request handlers. `UseUserNameAliases(mode)`
selects the registration and login alias resolver. MVC omits actions whose handlers are absent; a selected handler with broken dependencies fails at use.

`SchemataIdentityOptions.LoginUri` configures the [sign-in redirect](#sign-in-redirect).

## Extension points

| Interface                    | Purpose                                                                |
| ---------------------------- | ---------------------------------------------------------------------- |
| `IIdentityRequestAdvisor<TRequest,TResponse>` | Gate or transform any request before the handler runs; a handled advisor writes `IdentityOperationExecution<TResponse>.Response`. |
| `IMailSender<TUser>`         | Register an application implementation for confirmation and reset emails. |
| `IMessageSender<TUser>`      | Register an application implementation for SMS confirmation/reset codes. |
| `IUserAliasResolver`         | Replace registration and login key selection through `UseUserNameAliases`. |
| `IEmailNormalizer` / `IPhoneNumberNormalizer` | Replace the final manager-owned contact normalization. |
| `IIdentityAliasUniquenessAdvisor<TUser>` | Add ordered final-alias checks inside the manager's write transaction. |
| `SchemataUserManager<TUser>` | Subclass for domain-specific user operations.                          |

The selected send operation resolves its sender with `GetRequiredService`; a missing registration
throws when that operation uses the channel.

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
