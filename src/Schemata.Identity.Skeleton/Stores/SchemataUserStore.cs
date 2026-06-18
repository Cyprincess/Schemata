using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Skeleton.Stores;

public class SchemataUserStore<TUser> : SchemataUserStore<TUser, SchemataRole>
    where TUser : SchemataUser
{
    public SchemataUserStore(
        IRepository<TUser>             users,
        IRepository<SchemataRole>      roles,
        IRepository<SchemataUserClaim> userClaims,
        IRepository<SchemataUserRole>  userRole,
        IRepository<SchemataUserLogin> userLogins,
        IRepository<SchemataUserToken> userTokens,
        IdentityErrorDescriber?        describer = null
    ) : base(users, roles, userClaims, userRole, userLogins, userTokens, describer) { }
}

public class SchemataUserStore<TUser, TRole> : SchemataUserStore<TUser, TRole, SchemataUserClaim, SchemataUserRole,
    SchemataUserLogin, SchemataUserToken, SchemataRoleClaim>
    where TUser : SchemataUser
    where TRole : SchemataRole
{
    public SchemataUserStore(
        IRepository<TUser>             users,
        IRepository<TRole>             roles,
        IRepository<SchemataUserClaim> userClaims,
        IRepository<SchemataUserRole>  userRole,
        IRepository<SchemataUserLogin> userLogins,
        IRepository<SchemataUserToken> userTokens,
        IdentityErrorDescriber?        describer = null
    ) : base(users, roles, userClaims, userRole, userLogins, userTokens, describer) { }
}

/// <summary>
///     Repository-backed user store. Implements the Identity store interfaces directly:
///     every read goes through the <see cref="IRepository{TEntity}" /> advisor pipeline,
///     and <see cref="IQueryableUserStore{TUser}" /> is intentionally not implemented
///     because exposing a raw <see cref="IQueryable{T}" /> would bypass that pipeline.
/// </summary>
public class SchemataUserStore<TUser, TRole, TUserClaim, TUserRole, TUserLogin, TUserToken, TRoleClaim> :
    IUserLoginStore<TUser>, IUserClaimStore<TUser>, IUserPasswordStore<TUser>, IUserSecurityStampStore<TUser>,
    IUserEmailStore<TUser>, IUserLockoutStore<TUser>, IUserTwoFactorStore<TUser>, IUserAuthenticationTokenStore<TUser>,
    IUserAuthenticatorKeyStore<TUser>, IUserTwoFactorRecoveryCodeStore<TUser>, IUserRoleStore<TUser>,
    IUserCanonicalNameStore<TUser>, IUserDisplayNameStore<TUser>, IUserPhoneStore<TUser>,
    IUserPrincipalNameStore<TUser>, IProtectedUserStore<TUser>
    where TUser : SchemataUser
    where TRole : SchemataRole
    where TUserClaim : SchemataUserClaim, new()
    where TUserRole : SchemataUserRole, new()
    where TUserLogin : SchemataUserLogin, new()
    where TUserToken : SchemataUserToken, new()
    where TRoleClaim : SchemataRoleClaim, new()
{
    private const string InternalLoginProvider     = "[AspNetUserStore]";
    private const string AuthenticatorKeyTokenName = "AuthenticatorKey";
    private const string RecoveryCodeTokenName     = "RecoveryCodes";

    protected readonly IRepository<TRole> RolesRepository;

    protected readonly IRepository<TUserClaim> UserClaimsRepository;

    protected readonly IRepository<TUserLogin> UserLoginsRepository;

    protected readonly IRepository<TUserRole> UserRoleRepository;

    protected readonly IRepository<TUser> UsersRepository;

    protected readonly IRepository<TUserToken> UserTokensRepository;

    private bool _disposed;

    public SchemataUserStore(
        IRepository<TUser>      users,
        IRepository<TRole>      roles,
        IRepository<TUserClaim> userClaims,
        IRepository<TUserRole>  userRole,
        IRepository<TUserLogin> userLogins,
        IRepository<TUserToken> userTokens,
        IdentityErrorDescriber? describer = null
    ) {
        RolesRepository      = roles;
        UserClaimsRepository = userClaims;
        UserLoginsRepository = userLogins;
        UserRoleRepository   = userRole;
        UsersRepository      = users;
        UserTokensRepository = userTokens;
        ErrorDescriber       = describer ?? new IdentityErrorDescriber();
    }

    /// <summary>Provides localized error messages for identity operations.</summary>
    public IdentityErrorDescriber ErrorDescriber { get; set; }

    #region IUserAuthenticationTokenStore<TUser> Members

    public virtual async Task SetTokenAsync(
        TUser             user,
        string            loginProvider,
        string            name,
        string?           value,
        CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        var token = await FindTokenAsync(user, loginProvider, name, ct);
        if (token is null) {
            await UserTokensRepository.AddAsync(CreateUserToken(user, loginProvider, name, value), ct);
        } else {
            token.Value = value;
            await UserTokensRepository.UpdateAsync(token, ct);
        }
        await UserTokensRepository.CommitAsync(ct);
    }

    public virtual async Task RemoveTokenAsync(
        TUser             user,
        string            loginProvider,
        string            name,
        CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        var entry = await FindTokenAsync(user, loginProvider, name, ct);
        if (entry is null) {
            return;
        }

        await UserTokensRepository.RemoveAsync(entry, ct);
        await UserTokensRepository.CommitAsync(ct);
    }

    public virtual async Task<string?> GetTokenAsync(
        TUser             user,
        string            loginProvider,
        string            name,
        CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        var entry = await FindTokenAsync(user, loginProvider, name, ct);
        return entry?.Value;
    }

    #endregion

    #region IUserAuthenticatorKeyStore<TUser> Members

    public virtual Task SetAuthenticatorKeyAsync(TUser user, string key, CancellationToken ct = default) {
        return SetTokenAsync(user, InternalLoginProvider, AuthenticatorKeyTokenName, key, ct);
    }

    public virtual Task<string?> GetAuthenticatorKeyAsync(TUser user, CancellationToken ct = default) {
        return GetTokenAsync(user, InternalLoginProvider, AuthenticatorKeyTokenName, ct);
    }

    #endregion

    #region IUserCanonicalNameStore<TUser> Members

    public virtual async Task<TUser?> FindByCanonicalNameAsync(string canonicalName, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        return await UsersRepository.SingleOrDefaultAsync(q => q.Where(u => u.CanonicalName == canonicalName), ct);
    }

    #endregion

    #region IUserClaimStore<TUser> Members

    public virtual async Task<IList<Claim>> GetClaimsAsync(TUser user, CancellationToken ct = default) {
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        return await UserClaimsRepository.ListAsync(q => q.Where(uc => uc.UserId.Equals(user.Uid)), ct)
                                         .Map(c => c.ToClaim(), ct)
                                         .ToListAsync(ct);
    }

    public virtual async Task AddClaimsAsync(TUser user, IEnumerable<Claim> claims, CancellationToken ct = default) {
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        if (claims is null) {
            throw new ArgumentNullException(nameof(claims));
        }

        foreach (var claim in claims) {
            await UserClaimsRepository.AddAsync(CreateUserClaim(user, claim), ct);
        }

        await UserClaimsRepository.CommitAsync(ct);
    }

    public virtual async Task ReplaceClaimAsync(
        TUser             user,
        Claim             claim,
        Claim             newClaim,
        CancellationToken ct = default
    ) {
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        if (claim is null) {
            throw new ArgumentNullException(nameof(claim));
        }

        if (newClaim is null) {
            throw new ArgumentNullException(nameof(newClaim));
        }

        var claims = await UserClaimsRepository
                          .ListAsync(
                               q => q.Where(uc => uc.UserId.Equals(user.Uid)
                                               && uc.ClaimValue == claim.Value
                                               && uc.ClaimType == claim.Type), ct)
                          .ToListAsync(ct);
        foreach (var matched in claims) {
            matched.ClaimValue = newClaim.Value;
            matched.ClaimType  = newClaim.Type;
            await UserClaimsRepository.UpdateAsync(matched, ct);
        }

        await UserClaimsRepository.CommitAsync(ct);
    }

    public virtual async Task RemoveClaimsAsync(TUser user, IEnumerable<Claim> claims, CancellationToken ct = default) {
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        if (claims is null) {
            throw new ArgumentNullException(nameof(claims));
        }

        foreach (var claim in claims) {
            await foreach (var c in UserClaimsRepository.ListAsync(
                               q => q.Where(uc => uc.UserId.Equals(user.Uid)
                                               && uc.ClaimValue == claim.Value
                                               && uc.ClaimType == claim.Type), ct)) {
                await UserClaimsRepository.RemoveAsync(c, ct);
            }
        }

        await UserClaimsRepository.CommitAsync(ct);
    }

    public virtual async Task<IList<TUser>> GetUsersForClaimAsync(Claim claim, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (claim is null) {
            throw new ArgumentNullException(nameof(claim));
        }

        var users = await UserClaimsRepository
                         .ListAsync(
                              q => q.Where(uc => uc.ClaimValue == claim.Value && uc.ClaimType == claim.Type)
                                    .Select(uc => uc.UserId), ct)
                         .ToListAsync(ct);

        return await UsersRepository.ListAsync(q => q.Where(u => users.Contains(u.Uid)), ct).ToListAsync(ct);
    }

    #endregion

    #region IUserDisplayNameStore<TUser> Members

    public virtual Task<string?> GetDisplayNameAsync(TUser user, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        return Task.FromResult(user.DisplayName);
    }

    #endregion

    #region IUserEmailStore<TUser> Members

    public virtual Task SetEmailAsync(TUser user, string? email, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        user.Email = email;

        return Task.CompletedTask;
    }

    public virtual Task<string?> GetEmailAsync(TUser user, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        return Task.FromResult(user.Email);
    }

    public virtual Task<bool> GetEmailConfirmedAsync(TUser user, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        return Task.FromResult(user.EmailConfirmed);
    }

    public virtual Task SetEmailConfirmedAsync(TUser user, bool confirmed, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        user.EmailConfirmed = confirmed;

        return Task.CompletedTask;
    }

    public virtual async Task<TUser?> FindByEmailAsync(string normalizedEmail, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        return await UsersRepository.SingleOrDefaultAsync(q => q.Where(u => u.NormalizedEmail == normalizedEmail), ct);
    }

    public virtual Task<string?> GetNormalizedEmailAsync(TUser user, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        return Task.FromResult(user.NormalizedEmail);
    }

    public virtual Task SetNormalizedEmailAsync(TUser user, string? normalizedEmail, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        user.NormalizedEmail = normalizedEmail;

        return Task.CompletedTask;
    }

    #endregion

    #region IUserLockoutStore<TUser> Members

    public virtual Task<DateTimeOffset?> GetLockoutEndDateAsync(TUser user, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        return Task.FromResult(user.LockoutEnd);
    }

    public virtual Task SetLockoutEndDateAsync(TUser user, DateTimeOffset? lockoutEnd, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        user.LockoutEnd = lockoutEnd;

        return Task.CompletedTask;
    }

    public virtual Task<int> IncrementAccessFailedCountAsync(TUser user, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        user.AccessFailedCount++;

        return Task.FromResult(user.AccessFailedCount);
    }

    public virtual Task ResetAccessFailedCountAsync(TUser user, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        user.AccessFailedCount = 0;

        return Task.CompletedTask;
    }

    public virtual Task<int> GetAccessFailedCountAsync(TUser user, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        return Task.FromResult(user.AccessFailedCount);
    }

    public virtual Task<bool> GetLockoutEnabledAsync(TUser user, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        return Task.FromResult(user.LockoutEnabled);
    }

    public virtual Task SetLockoutEnabledAsync(TUser user, bool enabled, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        user.LockoutEnabled = enabled;

        return Task.CompletedTask;
    }

    #endregion

    #region IUserLoginStore<TUser> Members

    public virtual async Task<IdentityResult> CreateAsync(TUser user, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        await UsersRepository.AddAsync(user, ct);
        await UsersRepository.CommitAsync(ct);

        return IdentityResult.Success;
    }

    public virtual async Task<IdentityResult> UpdateAsync(TUser user, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        await UsersRepository.UpdateAsync(user, ct);
        try {
            await UsersRepository.CommitAsync(ct);
        } catch (ConcurrencyException) {
            return IdentityResult.Failed(ErrorDescriber.ConcurrencyFailure());
        }

        return IdentityResult.Success;
    }

    public virtual async Task<IdentityResult> DeleteAsync(TUser user, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        // Remove the user and every dependent row (role links, claims, logins, tokens) in one unit
        // of work so a failure cannot delete the user while leaving orphaned child rows behind.
        await using var uow = UsersRepository.Begin();
        UserRoleRepository.Join(uow);
        UserClaimsRepository.Join(uow);
        UserLoginsRepository.Join(uow);
        UserTokensRepository.Join(uow);

        await foreach (var role in UserRoleRepository.ListAsync(q => q.Where(ur => ur.UserId.Equals(user.Uid)), ct)) {
            await UserRoleRepository.RemoveAsync(role, ct);
        }

        await foreach (var claim in UserClaimsRepository.ListAsync(q => q.Where(uc => uc.UserId.Equals(user.Uid)), ct)) {
            await UserClaimsRepository.RemoveAsync(claim, ct);
        }

        await foreach (var login in UserLoginsRepository.ListAsync(q => q.Where(l => l.UserId.Equals(user.Uid)), ct)) {
            await UserLoginsRepository.RemoveAsync(login, ct);
        }

        await foreach (var token in UserTokensRepository.ListAsync(q => q.Where(t => t.UserId.Equals(user.Uid)), ct)) {
            await UserTokensRepository.RemoveAsync(token, ct);
        }

        await UsersRepository.RemoveAsync(user, ct);

        try {
            await uow.CommitAsync(ct);
        } catch (ConcurrencyException) {
            return IdentityResult.Failed(ErrorDescriber.ConcurrencyFailure());
        }

        return IdentityResult.Success;
    }

    public virtual Task<string> GetUserIdAsync(TUser user, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        return Task.FromResult(user.Uid.ToString());
    }

    public virtual Task<string?> GetUserNameAsync(TUser user, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        return Task.FromResult(user.UserName);
    }

    public virtual Task SetUserNameAsync(TUser user, string? userName, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        user.UserName = userName;

        return Task.CompletedTask;
    }

    public virtual Task<string?> GetNormalizedUserNameAsync(TUser user, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        return Task.FromResult(user.NormalizedUserName);
    }

    public virtual Task SetNormalizedUserNameAsync(TUser user, string? normalizedName, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        user.NormalizedUserName = normalizedName;

        return Task.CompletedTask;
    }

    public virtual async Task<TUser?> FindByIdAsync(string userId, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        var id = Guid.Parse(userId);
        return await UsersRepository.SingleOrDefaultAsync(q => q.Where(u => u.Uid == id), ct);
    }

    public virtual async Task<TUser?> FindByNameAsync(string normalizedUserName, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        return await UsersRepository.SingleOrDefaultAsync(q => q.Where(u => u.NormalizedUserName == normalizedUserName),
                                                          ct);
    }

    public virtual void Dispose() { _disposed = true; }

    public virtual async Task AddLoginAsync(TUser user, UserLoginInfo login, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        if (login is null) {
            throw new ArgumentNullException(nameof(login));
        }

        await UserLoginsRepository.AddAsync(CreateUserLogin(user, login), ct);
        await UserLoginsRepository.CommitAsync(ct);
    }

    public virtual async Task RemoveLoginAsync(
        TUser             user,
        string            loginProvider,
        string            providerKey,
        CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        var entry = await FindUserLoginAsync(user.Uid, loginProvider, providerKey, ct);
        if (entry is null) {
            return;
        }

        await UserLoginsRepository.RemoveAsync(entry, ct);
        await UserLoginsRepository.CommitAsync(ct);
    }

    public virtual async Task<IList<UserLoginInfo>> GetLoginsAsync(TUser user, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        return await UserLoginsRepository.ListAsync(q => q.Where(l => l.UserId.Equals(user.Uid)), ct)
                                         .Map(
                                              l => new UserLoginInfo(l.LoginProvider, l.ProviderKey,
                                                                     l.ProviderDisplayName), ct)
                                         .ToListAsync(ct);
    }

    public virtual async Task<TUser?> FindByLoginAsync(
        string            loginProvider,
        string            providerKey,
        CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        var userLogin = await FindUserLoginAsync(loginProvider, providerKey, ct);
        if (userLogin is not null) {
            return await FindUserAsync(userLogin.UserId, ct);
        }

        return null;
    }

    #endregion

    #region IUserPasswordStore<TUser> Members

    public virtual Task SetPasswordHashAsync(TUser user, string? passwordHash, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        user.PasswordHash = passwordHash;

        return Task.CompletedTask;
    }

    public virtual Task<string?> GetPasswordHashAsync(TUser user, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        return Task.FromResult(user.PasswordHash);
    }

    public virtual Task<bool> HasPasswordAsync(TUser user, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        return Task.FromResult(user.PasswordHash is not null);
    }

    #endregion

    #region IUserPhoneStore<TUser> Members

    public virtual Task SetPhoneNumberAsync(TUser user, string? phoneNumber, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        user.PhoneNumber = phoneNumber;

        return Task.CompletedTask;
    }

    public virtual Task<string?> GetPhoneNumberAsync(TUser user, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        return Task.FromResult(user.PhoneNumber);
    }

    public virtual Task<bool> GetPhoneNumberConfirmedAsync(TUser user, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        return Task.FromResult(user.PhoneNumberConfirmed);
    }

    public virtual Task SetPhoneNumberConfirmedAsync(TUser user, bool confirmed, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        user.PhoneNumberConfirmed = confirmed;

        return Task.CompletedTask;
    }

    public virtual async Task<TUser?> FindByPhoneAsync(string phone, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        return await UsersRepository.SingleOrDefaultAsync(q => q.Where(u => u.PhoneNumber == phone), ct);
    }

    #endregion

    #region IUserPrincipalNameStore<TUser> Members

    public virtual Task<string?> GetUserPrincipalNameAsync(TUser user, CancellationToken ct) {
        return GetNormalizedUserNameAsync(user, ct);
    }

    #endregion

    #region IUserRoleStore<TUser> Members

    public virtual async Task AddToRoleAsync(TUser user, string normalizedRoleName, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        if (string.IsNullOrWhiteSpace(normalizedRoleName)) {
            throw new ArgumentNullException(nameof(normalizedRoleName));
        }

        var roleEntity = await FindRoleAsync(normalizedRoleName, ct);
        if (roleEntity is null) {
            throw new InvalidOperationException(
                string.Format(SchemataResources.GetResourceString(SchemataResources.ST1011), "Role",
                              normalizedRoleName));
        }

        await UserRoleRepository.AddAsync(CreateUserRole(user, roleEntity), ct);
        await UserRoleRepository.CommitAsync(ct);
    }

    public virtual async Task RemoveFromRoleAsync(
        TUser             user,
        string            normalizedRoleName,
        CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        if (string.IsNullOrWhiteSpace(normalizedRoleName)) {
            throw new ArgumentNullException(nameof(normalizedRoleName));
        }

        var roleEntity = await FindRoleAsync(normalizedRoleName, ct);
        if (roleEntity is null) {
            return;
        }

        var userRole = await FindUserRoleAsync(user.Uid, roleEntity.Uid, ct);
        if (userRole is null) {
            return;
        }

        await UserRoleRepository.RemoveAsync(userRole, ct);
        await UserRoleRepository.CommitAsync(ct);
    }

    public virtual async Task<IList<string>> GetRolesAsync(TUser user, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        var roles = await UserRoleRepository
                         .ListAsync(q => q.Where(r => r.UserId == user.Uid).Select(r => r.RoleId), ct)
                         .ToListAsync(ct);

        return await RolesRepository.ListAsync(q => q.Where(r => roles.Contains(r.Uid)).Select(r => r.DisplayName!), ct)
                                    .ToListAsync(ct);
    }

    public virtual async Task<bool> IsInRoleAsync(
        TUser             user,
        string            normalizedRoleName,
        CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        if (string.IsNullOrWhiteSpace(normalizedRoleName)) {
            throw new ArgumentNullException(nameof(normalizedRoleName));
        }

        var role = await FindRoleAsync(normalizedRoleName, ct);
        if (role is null) {
            return false;
        }

        var userRole = await FindUserRoleAsync(user.Uid, role.Uid, ct);

        return userRole is not null;
    }

    public virtual async Task<IList<TUser>> GetUsersInRoleAsync(
        string            normalizedRoleName,
        CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(normalizedRoleName)) {
            throw new ArgumentNullException(nameof(normalizedRoleName));
        }

        var role = await FindRoleAsync(normalizedRoleName, ct);

        if (role is null) {
            return [];
        }

        var users = await UserRoleRepository
                         .ListAsync(q => q.Where(ur => ur.RoleId == role.Uid).Select(ur => ur.UserId), ct)
                         .ToListAsync(ct);

        return await UsersRepository.ListAsync(q => q.Where(u => users.Contains(u.Uid)), ct).ToListAsync(ct);
    }

    #endregion

    #region IUserSecurityStampStore<TUser> Members

    public virtual Task SetSecurityStampAsync(TUser user, string stamp, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        if (stamp is null) {
            throw new ArgumentNullException(nameof(stamp));
        }

        user.SecurityStamp = stamp;
        return Task.CompletedTask;
    }

    public virtual Task<string?> GetSecurityStampAsync(TUser user, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        return Task.FromResult(user.SecurityStamp);
    }

    #endregion

    #region IUserTwoFactorRecoveryCodeStore<TUser> Members

    public virtual Task ReplaceCodesAsync(
        TUser               user,
        IEnumerable<string> recoveryCodes,
        CancellationToken   ct = default
    ) {
        var mergedCodes = string.Join(";", recoveryCodes);
        return SetTokenAsync(user, InternalLoginProvider, RecoveryCodeTokenName, mergedCodes, ct);
    }

    public virtual async Task<bool> RedeemCodeAsync(TUser user, string code, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        if (string.IsNullOrEmpty(code)) {
            throw new ArgumentNullException(nameof(code));
        }

        var mergedCodes = await GetTokenAsync(user, InternalLoginProvider, RecoveryCodeTokenName, ct) ?? "";
        var splitCodes  = mergedCodes.Split(';');
        if (!splitCodes.Contains(code)) {
            return false;
        }

        var updatedCodes = splitCodes.Where(s => s != code);
        await ReplaceCodesAsync(user, updatedCodes, ct);
        return true;
    }

    public virtual async Task<int> CountCodesAsync(TUser user, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        var mergedCodes = await GetTokenAsync(user, InternalLoginProvider, RecoveryCodeTokenName, ct) ?? "";
        if (mergedCodes.Length == 0) {
            return 0;
        }

        return mergedCodes.AsSpan().Count(';') + 1;
    }

    #endregion

    #region IUserTwoFactorStore<TUser> Members

    public virtual Task SetTwoFactorEnabledAsync(TUser user, bool enabled, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        user.TwoFactorEnabled = enabled;
        return Task.CompletedTask;
    }

    public virtual Task<bool> GetTwoFactorEnabledAsync(TUser user, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        return Task.FromResult(user.TwoFactorEnabled);
    }

    #endregion

    protected virtual TUserClaim CreateUserClaim(TUser user, Claim claim) {
        var userClaim = new TUserClaim { UserId = user.Uid };
        userClaim.InitializeFromClaim(claim);
        return userClaim;
    }

    protected virtual TUserLogin CreateUserLogin(TUser user, UserLoginInfo login) {
        return new() {
            UserId              = user.Uid,
            ProviderKey         = login.ProviderKey,
            LoginProvider       = login.LoginProvider,
            ProviderDisplayName = login.ProviderDisplayName,
        };
    }

    protected virtual TUserToken CreateUserToken(
        TUser   user,
        string  loginProvider,
        string  name,
        string? value
    ) {
        return new() {
            UserId        = user.Uid,
            LoginProvider = loginProvider,
            Name          = name,
            Value         = value,
        };
    }

    protected virtual TUserRole CreateUserRole(TUser user, TRole role) {
        return new() { UserId = user.Uid, RoleId = role.Uid };
    }

    protected virtual async Task<TRole?> FindRoleAsync(string normalizedRoleName, CancellationToken ct) {
        return await RolesRepository.SingleOrDefaultAsync(q => q.Where(r => r.NormalizedName == normalizedRoleName),
                                                          ct);
    }

    protected virtual async Task<TUserRole?> FindUserRoleAsync(Guid userId, Guid roleId, CancellationToken ct) {
        return await UserRoleRepository.FindAsync([userId, roleId], ct);
    }

    protected virtual async Task<TUser?> FindUserAsync(Guid userId, CancellationToken ct) {
        return await UsersRepository.SingleOrDefaultAsync(q => q.Where(u => u.Uid == userId), ct);
    }

    protected virtual async Task<TUserLogin?> FindUserLoginAsync(
        Guid              userId,
        string            loginProvider,
        string            providerKey,
        CancellationToken ct
    ) {
        return await UserLoginsRepository.SingleOrDefaultAsync(
            q => q.Where(l => l.UserId.Equals(userId)
                           && l.LoginProvider == loginProvider
                           && l.ProviderKey == providerKey), ct);
    }

    protected virtual async Task<TUserLogin?> FindUserLoginAsync(
        string            loginProvider,
        string            providerKey,
        CancellationToken ct
    ) {
        return await UserLoginsRepository.FindAsync([loginProvider, providerKey], ct);
    }

    protected virtual async Task<TUserToken?> FindTokenAsync(
        TUser             user,
        string            loginProvider,
        string            name,
        CancellationToken ct
    ) {
        return await UserTokensRepository.FindAsync([user.Uid, loginProvider, name], ct);
    }

    protected virtual void ThrowIfDisposed() {
        if (!_disposed) {
            return;
        }

        throw new ObjectDisposedException(GetType().Name);
    }
}
