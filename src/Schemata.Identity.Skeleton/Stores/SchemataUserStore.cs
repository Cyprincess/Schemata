using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Skeleton.Stores;

public class SchemataUserStore : SchemataUserStore<SchemataUser>
{
    public SchemataUserStore(
        IRepository<SchemataUser>      users,
        IRepository<SchemataRole>      roles,
        IRepository<SchemataUserClaim> userClaims,
        IRepository<SchemataUserRole>  userRole,
        IRepository<SchemataUserLogin> userLogins,
        IRepository<SchemataUserToken> userTokens,
        IdentityErrorDescriber?        describer = null) : base(users,
                                                                roles,
                                                                userClaims,
                                                                userRole,
                                                                userLogins,
                                                                userTokens,
                                                                describer) { }
}

public class SchemataUserStore<TUser> : SchemataUserStore<TUser, SchemataRole> where TUser : SchemataUser
{
    public SchemataUserStore(
        IRepository<TUser>             users,
        IRepository<SchemataRole>      roles,
        IRepository<SchemataUserClaim> userClaims,
        IRepository<SchemataUserRole>  userRole,
        IRepository<SchemataUserLogin> userLogins,
        IRepository<SchemataUserToken> userTokens,
        IdentityErrorDescriber?        describer = null) : base(users,
                                                                roles,
                                                                userClaims,
                                                                userRole,
                                                                userLogins,
                                                                userTokens,
                                                                describer) { }
}

public class
    SchemataUserStore<TUser, TRole> : SchemataUserStore<TUser, TRole, SchemataUserClaim, SchemataUserRole,
    SchemataUserLogin, SchemataUserToken, SchemataRoleClaim> where TUser : SchemataUser
                                                             where TRole : SchemataRole
{
    public SchemataUserStore(
        IRepository<TUser>             users,
        IRepository<TRole>             roles,
        IRepository<SchemataUserClaim> userClaims,
        IRepository<SchemataUserRole>  userRole,
        IRepository<SchemataUserLogin> userLogins,
        IRepository<SchemataUserToken> userTokens,
        IdentityErrorDescriber?        describer = null) : base(users,
                                                                roles,
                                                                userClaims,
                                                                userRole,
                                                                userLogins,
                                                                userTokens,
                                                                describer) { }
}

public class SchemataUserStore<TUser, TRole, TUserClaim, TUserRole, TUserLogin, TUserToken, TRoleClaim> :
    UserStoreBase<TUser, TRole, long, TUserClaim, TUserRole, TUserLogin, TUserToken, TRoleClaim>,
    IUserDisplayNameStore<TUser>, IUserPhoneStore<TUser>, IUserPrincipalNameStore<TUser>,
    IProtectedUserStore<TUser> where TUser : SchemataUser
                               where TRole : SchemataRole
                               where TUserClaim : SchemataUserClaim, new()
                               where TUserRole : SchemataUserRole, new()
                               where TUserLogin : SchemataUserLogin, new()
                               where TUserToken : SchemataUserToken, new()
                               where TRoleClaim : SchemataRoleClaim, new()
{
    protected readonly IRepository<TRole>      RolesRepository;
    protected readonly IRepository<TUserClaim> UserClaimsRepository;
    protected readonly IRepository<TUserLogin> UserLoginsRepository;
    protected readonly IRepository<TUserRole>  UserRoleRepository;
    protected readonly IRepository<TUser>      UsersRepository;
    protected readonly IRepository<TUserToken> UserTokensRepository;

    public SchemataUserStore(
        IRepository<TUser>      users,
        IRepository<TRole>      roles,
        IRepository<TUserClaim> userClaims,
        IRepository<TUserRole>  userRole,
        IRepository<TUserLogin> userLogins,
        IRepository<TUserToken> userTokens,
        IdentityErrorDescriber? describer = null) : base(describer ?? new IdentityErrorDescriber()) {
        RolesRepository      = roles;
        UserClaimsRepository = userClaims;
        UserLoginsRepository = userLogins;
        UserRoleRepository   = userRole;
        UsersRepository      = users;
        UserTokensRepository = userTokens;
    }

    public virtual bool AutoSaveChanges { get; set; } = true;

    /// <inheritdoc />
    public override IQueryable<TUser> Users => UsersRepository.AsQueryable();

    #region IUserDisplayNameStore<TUser> Members

    public virtual Task<string?> GetDisplayNameAsync(TUser user, CancellationToken ct) {
        return Task.FromResult(user.DisplayName);
    }

    #endregion

    #region IUserPhoneStore<TUser> Members

    /// <inheritdoc />
    public override async Task<IdentityResult> CreateAsync(TUser user, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        await UsersRepository.AddAsync(user, ct);
        await SaveChanges(ct);
        return IdentityResult.Success;
    }

    /// <inheritdoc />
    public override async Task<IdentityResult> UpdateAsync(TUser user, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        await UsersRepository.UpdateAsync(user, ct);
        try {
            await SaveChanges(ct);
        } catch (ConcurrencyException) {
            return IdentityResult.Failed(ErrorDescriber.ConcurrencyFailure());
        }

        return IdentityResult.Success;
    }

    /// <inheritdoc />
    public override async Task<IdentityResult> DeleteAsync(TUser user, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        var roles = UserRoleRepository.ListAsync(q => q.Where(ur => ur.UserId.Equals(user.Id)), ct);
        await foreach (var role in roles) {
            await UserRoleRepository.RemoveAsync(role, ct);
        }

        await UserRoleRepository.CommitAsync(ct);

        await UsersRepository.RemoveAsync(user, ct);
        try {
            await SaveChanges(ct);
        } catch (ConcurrencyException) {
            return IdentityResult.Failed(ErrorDescriber.ConcurrencyFailure());
        }

        return IdentityResult.Success;
    }

#nullable disable
    /// <inheritdoc />
    public override async Task<TUser> FindByIdAsync(string userId, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        var id = ConvertIdFromString(userId);
        return await UsersRepository.SingleOrDefaultAsync(q => q.Where(u => u.Id == id), ct);
    }
#nullable restore

#nullable disable
    /// <inheritdoc />
    public override async Task<TUser> FindByNameAsync(string normalizedUserName, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        return await UsersRepository.SingleOrDefaultAsync(q => q.Where(u => u.NormalizedUserName == normalizedUserName), ct);
    }
#nullable restore

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

    protected virtual async Task SaveChanges(CancellationToken ct) {
        if (!AutoSaveChanges) {
            return;
        }

        await UsersRepository.CommitAsync(ct);
    }

#nullable disable
    /// <inheritdoc />
    protected override async Task<TRole> FindRoleAsync(string normalizedRoleName, CancellationToken ct) {
        return await RolesRepository.SingleOrDefaultAsync(q => q.Where(r => r.NormalizedName == normalizedRoleName), ct);
    }
#nullable restore

#nullable disable
    /// <inheritdoc />
    protected override async Task<TUserRole> FindUserRoleAsync(long userId, long roleId, CancellationToken ct) {
        return await UserRoleRepository.FindAsync([userId, roleId], ct).AsTask();
    }
#nullable restore

#nullable disable
    /// <inheritdoc />
    protected override async Task<TUser> FindUserAsync(long userId, CancellationToken ct) {
        return await UsersRepository.SingleOrDefaultAsync(q => q.Where(u => u.Id == userId), ct);
    }
#nullable restore

#nullable disable
    /// <inheritdoc />
    protected override async Task<TUserLogin> FindUserLoginAsync(
        long              userId,
        string            loginProvider,
        string            providerKey,
        CancellationToken ct) {
        return await UserLoginsRepository.SingleOrDefaultAsync(q => q.Where(l => l.UserId.Equals(userId)
                                                                              && l.LoginProvider == loginProvider
                                                                              && l.ProviderKey == providerKey),
                                                               ct);
    }
#nullable restore

#nullable disable
    /// <inheritdoc />
    protected override async Task<TUserLogin> FindUserLoginAsync(
        string            loginProvider,
        string            providerKey,
        CancellationToken ct) {
        return await UserLoginsRepository.SingleOrDefaultAsync(
            q => q.Where(l => l.LoginProvider == loginProvider && l.ProviderKey == providerKey),
            ct);
    }
#nullable restore

    /// <inheritdoc />
    public override async Task AddToRoleAsync(TUser user, string normalizedRoleName, CancellationToken ct = default) {
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
            throw new InvalidOperationException($"Role {normalizedRoleName} does not exist.");
        }

        await UserRoleRepository.AddAsync(CreateUserRole(user, roleEntity), ct);
        await UserRoleRepository.CommitAsync(ct);
    }

    /// <inheritdoc />
    public override async Task RemoveFromRoleAsync(
        TUser             user,
        string            normalizedRoleName,
        CancellationToken ct = default) {
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

        var userRole = await FindUserRoleAsync(user.Id, roleEntity.Id, ct);
        if (userRole is null) {
            return;
        }

        await UserRoleRepository.RemoveAsync(userRole, ct);
        await UserRoleRepository.CommitAsync(ct);
    }

    /// <inheritdoc />
    public override async Task<IList<string>> GetRolesAsync(TUser user, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        var userId = user.Id;

        var roles = await UserRoleRepository.ListAsync(q => q.Where(r => r.UserId == userId).Select(r => r.RoleId), ct)
                                            .ToListAsync(ct);

        return await RolesRepository.ListAsync(q => q.Where(r => roles.Contains(r.Id)).Select(r => r.DisplayName!), ct)
                                    .ToListAsync(ct);
    }

    /// <inheritdoc />
    public override async Task<bool> IsInRoleAsync(
        TUser             user,
        string            normalizedRoleName,
        CancellationToken ct = default) {
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

        var userRole = await FindUserRoleAsync(user.Id, role.Id, ct);
        return userRole is not null;
    }

    /// <inheritdoc />
    public override async Task<IList<Claim>> GetClaimsAsync(TUser user, CancellationToken ct = default) {
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        return await UserClaimsRepository.ListAsync(q => q.Where(uc => uc.UserId.Equals(user.Id)), ct)
                                         .Map(c => c.ToClaim(), ct)
                                         .ToListAsync(ct);
    }

    /// <inheritdoc />
    public override async Task AddClaimsAsync(TUser user, IEnumerable<Claim> claims, CancellationToken ct = default) {
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

    /// <inheritdoc />
    public override async Task ReplaceClaimAsync(
        TUser             user,
        Claim             claim,
        Claim             newClaim,
        CancellationToken ct = default) {
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

        var matchedClaims = await UserClaimsRepository
                                 .ListAsync(q => q.Where(uc => uc.UserId.Equals(user.Id)
                                                            && uc.ClaimValue == claim.Value
                                                            && uc.ClaimType == claim.Type),
                                            ct)
                                 .ToListAsync(ct);
        foreach (var matchedClaim in matchedClaims) {
            matchedClaim.ClaimValue = newClaim.Value;
            matchedClaim.ClaimType  = newClaim.Type;
        }
    }

    /// <inheritdoc />
    public override async Task RemoveClaimsAsync(
        TUser              user,
        IEnumerable<Claim> claims,
        CancellationToken  ct = default) {
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        if (claims is null) {
            throw new ArgumentNullException(nameof(claims));
        }

        foreach (var claim in claims) {
            var matchedClaims = UserClaimsRepository.ListAsync(
                q => q.Where(uc => uc.UserId.Equals(user.Id)
                                && uc.ClaimValue == claim.Value
                                && uc.ClaimType == claim.Type),
                ct);
            await foreach (var c in matchedClaims) {
                await UserClaimsRepository.RemoveAsync(c, ct);
            }

            await UserClaimsRepository.CommitAsync(ct);
        }
    }

    /// <inheritdoc />
    public override async Task AddLoginAsync(TUser user, UserLoginInfo login, CancellationToken ct = default) {
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

    /// <inheritdoc />
    public override async Task RemoveLoginAsync(
        TUser             user,
        string            loginProvider,
        string            providerKey,
        CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        var entry = await FindUserLoginAsync(user.Id, loginProvider, providerKey, ct);
        if (entry is null) {
            return;
        }

        await UserLoginsRepository.RemoveAsync(entry, ct);
        await UserLoginsRepository.CommitAsync(ct);
    }

    /// <inheritdoc />
    public override async Task<IList<UserLoginInfo>> GetLoginsAsync(TUser user, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user is null) {
            throw new ArgumentNullException(nameof(user));
        }

        var userId = user.Id;
        return await UserLoginsRepository.ListAsync(q => q.Where(l => l.UserId.Equals(userId)), ct)
                                         .Map(l => new UserLoginInfo(l.LoginProvider,
                                                                     l.ProviderKey,
                                                                     l.ProviderDisplayName),
                                              ct)
                                         .ToListAsync(ct);
    }

#nullable disable
    /// <inheritdoc />
    public override async Task<TUser> FindByLoginAsync(
        string            loginProvider,
        string            providerKey,
        CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        var userLogin = await FindUserLoginAsync(loginProvider, providerKey, ct);
        if (userLogin is not null) {
            return await FindUserAsync(userLogin.UserId, ct);
        }

        return null;
    }
#nullable restore

#nullable disable
    /// <inheritdoc />
    public override async Task<TUser> FindByEmailAsync(string normalizedEmail, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        return await UsersRepository.SingleOrDefaultAsync(q => q.Where(u => u.NormalizedEmail == normalizedEmail), ct);
    }
#nullable restore

    /// <inheritdoc />
    public override async Task<IList<TUser>> GetUsersForClaimAsync(Claim claim, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (claim is null) {
            throw new ArgumentNullException(nameof(claim));
        }

        var users = await UserClaimsRepository
                         .ListAsync(q => q.Where(uc => uc.ClaimValue == claim.Value && uc.ClaimType == claim.Type)
                                          .Select(uc => uc.UserId),
                                    ct)
                         .ToListAsync(ct);

        return await UsersRepository.ListAsync(q => q.Where(u => users.Contains(u.Id)), ct).ToListAsync(ct);
    }

    /// <inheritdoc />
    public override async Task<IList<TUser>> GetUsersInRoleAsync(
        string            normalizedRoleName,
        CancellationToken ct = default) {
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
                         .ListAsync(q => q.Where(ur => ur.RoleId == role.Id).Select(ur => ur.UserId), ct)
                         .ToListAsync(ct);

        return await UsersRepository.ListAsync(q => q.Where(u => users.Contains(u.Id)), ct).ToListAsync(ct);
    }

#nullable disable
    /// <inheritdoc />
    protected override Task<TUserToken> FindTokenAsync(
        TUser             user,
        string            loginProvider,
        string            name,
        CancellationToken ct) {
        return UserTokensRepository.FindAsync([user.Id, loginProvider, name], ct).AsTask();
    }
#nullable restore

    /// <inheritdoc />
    protected override async Task AddUserTokenAsync(TUserToken token) {
        await UserTokensRepository.AddAsync(token);
        await UserTokensRepository.CommitAsync();
    }

    /// <inheritdoc />
    protected override async Task RemoveUserTokenAsync(TUserToken token) {
        await UserTokensRepository.RemoveAsync(token);
        await UserTokensRepository.CommitAsync();
    }
}
