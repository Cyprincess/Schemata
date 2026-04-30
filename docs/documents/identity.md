# Identity

Schemata wraps ASP.NET Core Identity with its own entity types, user manager extensions, and a controller that exposes registration, login, token refresh, profile management, password reset, account confirmation, and two-factor authentication as JSON API endpoints. Schemata does not provide login or registration UI -- these are implemented by the application. All endpoints are headless APIs suitable for any frontend (SPA, mobile app, server-rendered pages).

## Packages

| Package                        | Role                                                              |
| ------------------------------ | ----------------------------------------------------------------- |
| `Schemata.Identity.Skeleton`   | Entity types, stores, managers, request/response models, services |
| `Schemata.Identity.Foundation` | Feature, controller, builder extensions, advisor interfaces       |

## Entity types

### SchemataUser

Extends `IdentityUser<long>` with Schemata interfaces (`IIdentifier`, `ICanonicalName`, `IDescriptive`, `IConcurrency`, `ITimestamp`). Stored in the `SchemataUsers` table. Uses a `long` primary key.

Key properties beyond standard Identity fields:

- `Name`, `CanonicalName` -- canonical name support (`users/{user}`)
- `DisplayName`, `DisplayNames` -- display name with localization
- `Timestamp` -- `Guid` used for optimistic concurrency (mapped to `ConcurrencyStamp`)
- `CreateTime`, `UpdateTime` -- audit timestamps

### SchemataRole

Extends `IdentityRole<long>` with the same Schemata interfaces. Stored in the `SchemataRoles` table. Canonical name pattern: `roles/{role}`.

## SchemataUserManager\<TUser\>

Extends the standard `UserManager<TUser>` with:

- `GetDisplayNameAsync(user)` -- retrieves the display name via `IUserDisplayNameStore<TUser>`
- `GetUserPrincipalNameAsync(user)` -- retrieves the UPN via `IUserPrincipalNameStore<TUser>`
- `FindByPhoneAsync(phone)` -- finds a user by phone number via `IUserPhoneStore<TUser>`, with support for personal data protection key rotation
- `ToClaimsAsync(user)` -- projects the user into a `ClaimsStore` containing `NameIdentifier`, `Upn`, `Email`, `MobilePhone`, `Name`, and `Role` claims

## UseIdentity()

```csharp
builder.UseIdentity(
    identify:  options => { ... },  // SchemataIdentityOptions
    configure: options => { ... },  // ASP.NET Core IdentityOptions
    build:     builder => { ... },  // IdentityBuilder for adding token providers, etc.
    bearer:    options => { ... }   // BearerTokenOptions
);
```

### Overloads

- `UseIdentity()` -- uses `SchemataUser` and `SchemataRole` with `SchemataUserStore` and `SchemataRoleStore`
- `UseIdentity<TUser, TRole>()` -- uses custom user/role types with default stores
- `UseIdentity<TUser, TRole, TUserStore, TRoleStore>()` -- fully custom types

### Feature behavior

`SchemataIdentityFeature` depends on `SchemataAuthenticationFeature` and `SchemataControllersFeature`. It registers:

- A composite authentication handler (`Identity.BearerAndApplication`) that tries bearer token authentication first, then falls back to cookie authentication
- Bearer token authentication via `AddBearerToken`
- Identity cookies via `AddIdentityCookies`
- `IMailSender<T>` and `IMessageSender<T>` with no-op defaults (`NoOpMailSender`, `NoOpMessageSender`)
- `IUserStore<TUser>` and `IRoleStore<TRole>` as scoped services
- Identity core services with `SchemataUserManager<TUser>`, roles, sign-in manager, and default token providers
- `ClaimStoreJsonConverter` for JSON serialization of `ClaimsStore` objects

## SchemataIdentityOptions

Controls which identity endpoints are enabled:

| Property                       | Default | Description                          |
| ------------------------------ | ------- | ------------------------------------ |
| `AllowRegistration`            | `true`  | Enable the Register endpoint         |
| `AllowAccountConfirmation`     | `true`  | Enable email/phone confirmation      |
| `AllowPasswordReset`           | `true`  | Enable forgot/reset password flow    |
| `AllowPasswordChange`          | `true`  | Enable authenticated password change |
| `AllowEmailChange`             | `true`  | Enable email address change          |
| `AllowPhoneNumberChange`       | `true`  | Enable phone number change           |
| `AllowTwoFactorAuthentication` | `true`  | Enable 2FA management endpoints      |

## AuthenticateController

Mounted at `~/Authenticate`. All endpoints are API endpoints (`[ApiController]`).

### Registration: POST ~/Authenticate/Register

Runs a three-phase advisor pipeline:

1. **IIdentityRegisterRequestAdvisor** -- validates/transforms the `RegisterRequest` before user creation. Receives `(RegisterRequest, HttpContext)`. Block/Handle suppresses registration.

2. **IIdentityRegisterUserAdvisor** -- runs after the `SchemataUser` is constructed from the request but before persistence. Receives `(SchemataUser, HttpContext)`. Use it to set default values or reject.

3. **IIdentityRegisterAdvisor** -- runs after the user is successfully created. Receives `(SchemataUser, HttpContext)`. Use it for post-registration tasks like role assignment or welcome notifications.

If `RequireConfirmedAccount` is enabled, a confirmation code is sent after creation.

### Login: POST ~/Authenticate/Login

Authenticates with username/password. Supports two-factor via `TwoFactorCode` or `TwoFactorRecoveryCode` fields. Returns a bearer token on success.

### Refresh: POST ~/Authenticate/Refresh

Exchanges a valid refresh token for a new authentication ticket. Returns a challenge result if the token is invalid or expired.

### Profile: GET ~/Account/Profile

Returns the authenticated user's claims as a `ClaimsStore`. Requires `[Authorize]`.

### Email change: PUT ~/Account/Profile/Email

Sends a confirmation code for the new email address. Requires `[Authorize]`.

### Phone change: PUT ~/Account/Profile/Phone

Sends a confirmation code for the new phone number. Requires `[Authorize]`.

### Password change: PUT ~/Account/Profile/Password

Changes the current user's password with old/new password verification. Requires `[Authorize]`.

### Forgot: POST ~/Authenticate/Forgot

Sends a password reset code to the user's confirmed email or phone. Returns 202 Accepted regardless of user existence to prevent enumeration.

### Reset: POST ~/Authenticate/Reset

Resets the password using the code from the Forgot endpoint.

### Confirm: GET ~/Authenticate/Confirm

Confirms an email or phone number change using a verification code passed as query parameters (`email`/`phone` + `code`).

### Code: POST ~/Authenticate/Code

Sends a new confirmation code. Returns 202 Accepted regardless of user existence.

### Authenticator: GET ~/Authenticate/Authenticator

Returns two-factor authenticator status. Generates a new shared key and recovery codes if not yet enabled. Requires `[Authorize]`.

### Enroll: POST ~/Authenticate/Authenticator

Enables two-factor authentication after verifying a TOTP code. Requires `[Authorize]`.

### Downgrade: PATCH ~/Authenticate/Authenticator

Disables two-factor authentication after verifying a TOTP or recovery code. Resets the authenticator key. Requires `[Authorize]`.
