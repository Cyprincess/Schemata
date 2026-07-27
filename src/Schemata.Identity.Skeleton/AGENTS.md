# Schemata.Identity.Skeleton

## OVERVIEW

49 files, ~2806 LOC. **This package breaks the usual `Skeleton` = contracts-only rule, on purpose.** It ships concrete `SchemataUserStore`, `SchemataRoleStore` and `SchemataUserManager<TUser>` alongside the contracts, and it legitimately depends on ASP.NET (`Microsoft.Extensions.Identity.Stores`) because it *is* the bridge to ASP.NET Core Identity. Contrast `Schemata.Authorization.Skeleton`, which is contract-only. Deps: `Schemata.Entity.Repository` + `Microsoft.Extensions.Identity.Stores`.

## STRUCTURE

| Folder | Role |
|---|---|
| `Entities/` | 7 `Identity*<Guid>` descendants |
| `Stores/` | 4 store interfaces **plus** the concrete `SchemataUserStore` / `SchemataRoleStore` |
| `Managers/` | `SchemataUserManager<TUser>` — the only concrete manager in any Skeleton |
| `Advisors/` | 10 advisor interfaces for register / login / refresh / profile / recovery / 2FA |
| `Services/` | `IMailSender<TUser>` / `IMessageSender<TUser>` + sealed `NoOp*` defaults |
| `Claims/` + `Json/` | `ClaimStore`, `ClaimsStore`, `ClaimStoreJsonConverter` |
| `Models/` | 17 DTO records |
| root | `IdentityOperation` (15 values), `IdentityStatus`, `IdentityResult<T>` |

## ENTITIES

All `Guid`-keyed — never the ASP.NET-default `string`. `SchemataUser` (`IdentityUser<Guid>` + `IIdentifier`/`ICanonicalName`/`IDescriptive`/`IConcurrency`/`ITimestamp`), `SchemataRole`, `SchemataUserClaim`, `SchemataRoleClaim`, `SchemataUserLogin`, `SchemataUserToken`, `SchemataUserRole`.

`SchemataUser.Id` and `ConcurrencyStamp` are `[NotMapped]` and delegate to `Uid` / `Timestamp`. Marking either as mapped duplicates the column.

## STORES AND MANAGER

- Interfaces shipped here: `IUserCanonicalNameStore<TUser>`, `IUserPrincipalNameStore<TUser>`, `IUserDisplayNameStore<TUser>`, `IUserPhoneStore<TUser>` (extends ASP.NET `IUserPhoneNumberStore`).
- [Stores/SchemataUserStore.cs](Stores/SchemataUserStore.cs) — three arities: `<TUser>`, `<TUser,TRole>`, and the 7-arity base `<TUser,TRole,TUserClaim,TUserRole,TUserLogin,TUserToken,TRoleClaim>` implementing ~14 ASP.NET Identity store interfaces. The shorter overloads default the link entities.
- [Stores/SchemataRoleStore.cs](Stores/SchemataRoleStore.cs) — `<TRole>` and the 3-arity base `<TRole,TRoleClaim,TUserRole>`.
- [Managers/SchemataUserManager.cs](Managers/SchemataUserManager.cs) — subclasses `UserManager<TUser>`, adds `GetDisplayNameAsync`, `GetUserPrincipalNameAsync`, `FindByCanonicalNameAsync`, `FindByPhoneAsync` (which falls through `ILookupProtectorKeyRing` when `IdentityOptions.Stores.ProtectPersonalData` is on).

## ADVISOR CONTRACTS

`IIdentityRequestAdvisor<T>` (3: `T`, `IdentityOperation`, `ClaimsPrincipal`) · `IIdentityRegisterAdvisor<TUser>` (2) · `IIdentityRegisterUserAdvisor` (2, closed) · `IIdentityLoginAdvisor` (2, closed) · `IIdentityRefreshAdvisor` (1, closed over `ClaimsPrincipal`) · `IIdentityRefreshUserAdvisor<TUser>` (1) · `IIdentityProfileChangeAdvisor` (2, closed) · `IIdentityProfileResponseAdvisor<TUser>` (3: `TUser`, `ClaimsStore`, `ClaimsPrincipal`) · `IIdentityRecoveryAdvisor` (2, closed) · `IIdentityTwoFactorAdvisor` (2, closed).

## GOTCHAS

- `IdentityResult<T>` here is NOT `Microsoft.AspNetCore.Identity.IdentityResult`. Two different types with the same simple name coexist: the stores return the ASP.NET one, the advisor/handler surface returns this one (`Success(data) | Challenge()`). They are not interchangeable.
- `SchemataUserStore.CreateAsync` generates `Uid` via `Identifiers.NewUid()` and stamps `user.Name = user.Uid.ToString()` before adding, so the canonical name is available immediately after creation — do not set it yourself.
- `SchemataUserStore.DeleteAsync` and `SchemataRoleStore.DeleteAsync` open a unit of work (`Begin()`) and `Join(uow)` the related repositories before deleting child rows. Deleting children outside that unit orphans rows on partial failure.
- `FindByIdAsync` strips a leading canonical-name segment before parsing the `Guid`, so both `users/{uid}` and a bare Guid string resolve.
- Authenticator key and recovery codes are stored in `SchemataUserToken` under the sentinel login provider `[AspNetUserStore]`, names `AuthenticatorKey` / `RecoveryCodes`. The codes are semicolon-joined; the count is derived by counting separators rather than splitting.
- `ClaimStoreJsonConverter` accepts a JSON string OR a string array on read, and emits a bare string when the store holds exactly one value. Round-tripping a single-element array yields a scalar.
- `IProtectedUserStore<TUser>` appears in `SchemataUserStore`'s base list but is not defined anywhere in this repository — it comes from `Microsoft.Extensions.Identity.Stores`. Do not go hunting for it in `src/`.

Canonical docs: `docs/documents/identity.md`, `docs/guides/identity.md`.
