using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Schemata.Entity.Repository;

namespace Schemata.Identity.Skeleton.Stores;

public class SchemataUserStore : SchemataUserStore<IdentityUser<long>>
{
    public SchemataUserStore(
        IRepository<IdentityUser<long>> users,
        IRepository<IdentityRole<long>> roles,
        IRepository<IdentityUserClaim<long>> userClaims,
        IRepository<IdentityUserRole<long>> userRoles,
        IRepository<IdentityUserLogin<long>> userLogins,
        IRepository<IdentityUserToken<long>> userTokens,
        IdentityErrorDescriber describer = null) : base(users, roles, userClaims, userRoles, userLogins, userTokens,
        describer) { }
}

public class SchemataUserStore<TUser> : SchemataUserStore<TUser, IdentityRole<long>>
    where TUser : IdentityUser<long>
{
    public SchemataUserStore(
        IRepository<TUser> users,
        IRepository<IdentityRole<long>> roles,
        IRepository<IdentityUserClaim<long>> userClaims,
        IRepository<IdentityUserRole<long>> userRoles,
        IRepository<IdentityUserLogin<long>> userLogins,
        IRepository<IdentityUserToken<long>> userTokens,
        IdentityErrorDescriber describer = null) : base(users, roles, userClaims, userRoles, userLogins, userTokens,
        describer) { }
}

public class SchemataUserStore<TUser, TRole> : SchemataUserStore<TUser, TRole, IdentityUserClaim<long>,
    IdentityUserRole<long>, IdentityUserLogin<long>, IdentityUserToken<long>, IdentityRoleClaim<long>>
    where TUser : IdentityUser<long>
    where TRole : IdentityRole<long>
{
    public SchemataUserStore(
        IRepository<TUser> users,
        IRepository<TRole> roles,
        IRepository<IdentityUserClaim<long>> userClaims,
        IRepository<IdentityUserRole<long>> userRoles,
        IRepository<IdentityUserLogin<long>> userLogins,
        IRepository<IdentityUserToken<long>> userTokens,
        IdentityErrorDescriber describer = null) : base(users, roles, userClaims, userRoles, userLogins, userTokens,
        describer) { }
}

public class SchemataUserStore<TUser, TRole, TUserClaim, TUserRole, TUserLogin, TUserToken, TRoleClaim> :
    UserStoreBase<TUser, TRole, long, TUserClaim, TUserRole, TUserLogin, TUserToken, TRoleClaim>,
    IProtectedUserStore<TUser>
    where TUser : IdentityUser<long>
    where TRole : IdentityRole<long>
    where TUserClaim : IdentityUserClaim<long>, new()
    where TUserRole : IdentityUserRole<long>, new()
    where TUserLogin : IdentityUserLogin<long>, new()
    where TUserToken : IdentityUserToken<long>, new()
    where TRoleClaim : IdentityRoleClaim<long>, new()
{
    protected readonly IRepository<TRole>      RolesRepository;
    protected readonly IRepository<TUserClaim> UserClaimsRepository;
    protected readonly IRepository<TUserLogin> UserLoginsRepository;
    protected readonly IRepository<TUserRole>  UserRolesRepository;
    protected readonly IRepository<TUser>      UsersRepository;
    protected readonly IRepository<TUserToken> UserTokensRepository;

    public SchemataUserStore(
        IRepository<TUser>      users,
        IRepository<TRole>      roles,
        IRepository<TUserClaim> userClaims,
        IRepository<TUserRole>  userRoles,
        IRepository<TUserLogin> userLogins,
        IRepository<TUserToken> userTokens,
        IdentityErrorDescriber  describer = null) : base(describer ?? new IdentityErrorDescriber()) {
        UsersRepository      = users;
        RolesRepository      = roles;
        UserClaimsRepository = userClaims;
        UserRolesRepository  = userRoles;
        UserLoginsRepository = userLogins;
        UserTokensRepository = userTokens;
    }

    public bool AutoSaveChanges { get; set; } = true;

    /// <inheritdoc />
    public override IQueryable<TUser> Users => UsersRepository.AsQueryable();

    #region IProtectedUserStore<TUser> Members

    /// <inheritdoc />
    public override async Task<IdentityResult> CreateAsync(TUser user, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user == null) {
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
        if (user == null) {
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
        if (user == null) {
            throw new ArgumentNullException(nameof(user));
        }

        await UsersRepository.RemoveAsync(user, ct);
        try {
            await SaveChanges(ct);
        } catch (ConcurrencyException) {
            return IdentityResult.Failed(ErrorDescriber.ConcurrencyFailure());
        }

        return IdentityResult.Success;
    }

    /// <inheritdoc />
    public override async Task<TUser> FindByIdAsync(string userId, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        var id = ConvertIdFromString(userId);
        return await UsersRepository.SingleOrDefaultAsync(q => q.Where(u => u.Id == id), ct);
    }

    /// <inheritdoc />
    public override async Task<TUser> FindByNameAsync(string normalizedUserName, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        return await UsersRepository.SingleOrDefaultAsync(q => q.Where(u => u.NormalizedUserName == normalizedUserName),
            ct);
    }

    #endregion

    protected async Task SaveChanges(CancellationToken ct) {
        if (AutoSaveChanges) {
            await UsersRepository.CommitAsync(ct);
        }
    }

    /// <inheritdoc />
    protected override async Task<TRole> FindRoleAsync(string normalizedRoleName, CancellationToken ct) {
        return await RolesRepository.SingleOrDefaultAsync(q => q.Where(r => r.NormalizedName == normalizedRoleName),
            ct);
    }

    /// <inheritdoc />
    protected override async Task<TUserRole> FindUserRoleAsync(long userId, long roleId, CancellationToken ct) {
        return await UserRolesRepository.FindAsync([userId, roleId], ct).AsTask();
    }

    /// <inheritdoc />
    protected override async Task<TUser> FindUserAsync(long userId, CancellationToken ct) {
        return await UsersRepository.SingleOrDefaultAsync(q => q.Where(u => u.Id == userId), ct);
    }

    /// <inheritdoc />
    protected override async Task<TUserLogin> FindUserLoginAsync(
        long              userId,
        string            loginProvider,
        string            providerKey,
        CancellationToken ct) {
        return await UserLoginsRepository.SingleOrDefaultAsync(
            q => q.Where(l => l.UserId.Equals(userId)
                           && l.LoginProvider == loginProvider
                           && l.ProviderKey == providerKey), ct);
    }

    /// <inheritdoc />
    protected override async Task<TUserLogin> FindUserLoginAsync(
        string            loginProvider,
        string            providerKey,
        CancellationToken ct) {
        return await UserLoginsRepository.SingleOrDefaultAsync(
            q => q.Where(l => l.LoginProvider == loginProvider && l.ProviderKey == providerKey), ct);
    }

    /// <inheritdoc />
    public override async Task AddToRoleAsync(TUser user, string normalizedRoleName, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user == null) {
            throw new ArgumentNullException(nameof(user));
        }

        if (string.IsNullOrWhiteSpace(normalizedRoleName)) {
            throw new ArgumentNullException(nameof(normalizedRoleName));
        }

        var roleEntity = await FindRoleAsync(normalizedRoleName, ct);
        if (roleEntity == null) {
            throw new InvalidOperationException($"Role {normalizedRoleName} does not exist.");
        }

        await UserRolesRepository.AddAsync(CreateUserRole(user, roleEntity), ct);
        await UserRolesRepository.CommitAsync(ct);
    }

    /// <inheritdoc />
    public override async Task RemoveFromRoleAsync(
        TUser             user,
        string            normalizedRoleName,
        CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user == null) {
            throw new ArgumentNullException(nameof(user));
        }

        if (string.IsNullOrWhiteSpace(normalizedRoleName)) {
            throw new ArgumentNullException(nameof(normalizedRoleName));
        }

        var roleEntity = await FindRoleAsync(normalizedRoleName, ct);
        if (roleEntity == null) {
            return;
        }

        var userRole = await FindUserRoleAsync(user.Id, roleEntity.Id, ct);
        if (userRole == null) {
            return;
        }

        await UserRolesRepository.RemoveAsync(userRole, ct);
        await UserRolesRepository.CommitAsync(ct);
    }

    /// <inheritdoc />
    public override async Task<IList<string>> GetRolesAsync(TUser user, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user == null) {
            throw new ArgumentNullException(nameof(user));
        }

        var userId = user.Id;

        var roles = await UserRolesRepository.ListAsync(q => q.Where(r => r.UserId == userId).Select(r => r.RoleId), ct)
                                             .ToListAsync(ct);

        return await RolesRepository.ListAsync(q => q.Where(r => roles.Contains(r.Id)).Select(r => r.Name!), ct)
                                    .ToListAsync(ct);
    }

    /// <inheritdoc />
    public override async Task<bool> IsInRoleAsync(
        TUser             user,
        string            normalizedRoleName,
        CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user == null) {
            throw new ArgumentNullException(nameof(user));
        }

        if (string.IsNullOrWhiteSpace(normalizedRoleName)) {
            throw new ArgumentNullException(nameof(normalizedRoleName));
        }

        var role = await FindRoleAsync(normalizedRoleName, ct);
        if (role == null) {
            return false;
        }

        var userRole = await FindUserRoleAsync(user.Id, role.Id, ct);
        return userRole != null;
    }

    /// <inheritdoc />
    public override async Task<IList<Claim>> GetClaimsAsync(TUser user, CancellationToken ct = default) {
        ThrowIfDisposed();
        if (user == null) {
            throw new ArgumentNullException(nameof(user));
        }

        return await UserClaimsRepository.ListAsync(q => q.Where(uc => uc.UserId.Equals(user.Id)), ct)
                                         .Map(c => c.ToClaim(), ct)
                                         .ToListAsync(ct);
    }

    /// <inheritdoc />
    public override async Task AddClaimsAsync(TUser user, IEnumerable<Claim> claims, CancellationToken ct = default) {
        ThrowIfDisposed();
        if (user == null) {
            throw new ArgumentNullException(nameof(user));
        }

        if (claims == null) {
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
        if (user == null) {
            throw new ArgumentNullException(nameof(user));
        }

        if (claim == null) {
            throw new ArgumentNullException(nameof(claim));
        }

        if (newClaim == null) {
            throw new ArgumentNullException(nameof(newClaim));
        }

        var matchedClaims = await UserClaimsRepository
                                 .ListAsync(
                                      q => q.Where(uc
                                          => uc.UserId.Equals(user.Id)
                                          && uc.ClaimValue == claim.Value
                                          && uc.ClaimType == claim.Type), ct)
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
        if (user == null) {
            throw new ArgumentNullException(nameof(user));
        }

        if (claims == null) {
            throw new ArgumentNullException(nameof(claims));
        }

        foreach (var claim in claims) {
            var matchedClaims = UserClaimsRepository.ListAsync(
                q => q.Where(uc
                    => uc.UserId.Equals(user.Id) && uc.ClaimValue == claim.Value && uc.ClaimType == claim.Type), ct);
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
        if (user == null) {
            throw new ArgumentNullException(nameof(user));
        }

        if (login == null) {
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
        if (user == null) {
            throw new ArgumentNullException(nameof(user));
        }

        var entry = await FindUserLoginAsync(user.Id, loginProvider, providerKey, ct);
        if (entry == null) {
            return;
        }

        await UserLoginsRepository.RemoveAsync(entry, ct);
        await UserLoginsRepository.CommitAsync(ct);
    }

    /// <inheritdoc />
    public override async Task<IList<UserLoginInfo>> GetLoginsAsync(TUser user, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (user == null) {
            throw new ArgumentNullException(nameof(user));
        }

        var userId = user.Id;
        return await UserLoginsRepository.ListAsync(q => q.Where(l => l.UserId.Equals(userId)), ct)
                                         .Map(
                                              l => new UserLoginInfo(l.LoginProvider, l.ProviderKey,
                                                  l.ProviderDisplayName), ct)
                                         .ToListAsync(ct);
    }

    /// <inheritdoc />
    public override async Task<TUser> FindByLoginAsync(
        string            loginProvider,
        string            providerKey,
        CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        var userLogin = await FindUserLoginAsync(loginProvider, providerKey, ct);
        if (userLogin != null) {
            return await FindUserAsync(userLogin.UserId, ct);
        }

        return null;
    }

    /// <inheritdoc />
    public override async Task<TUser> FindByEmailAsync(string normalizedEmail, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        return await UsersRepository.SingleOrDefaultAsync(q => q.Where(u => u.NormalizedEmail == normalizedEmail), ct);
    }

    /// <inheritdoc />
    public override async Task<IList<TUser>> GetUsersForClaimAsync(Claim claim, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (claim == null) {
            throw new ArgumentNullException(nameof(claim));
        }

        var users = await UserClaimsRepository
                         .ListAsync(
                              q => q.Where(uc => uc.ClaimValue == claim.Value && uc.ClaimType == claim.Type)
                                    .Select(uc => uc.UserId), ct)
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

        if (role == null) {
            return [];
        }

        var users = await UserRolesRepository
                         .ListAsync(q => q.Where(ur => ur.RoleId == role.Id).Select(ur => ur.UserId), ct)
                         .ToListAsync(ct);

        return await UsersRepository.ListAsync(q => q.Where(u => users.Contains(u.Id)), ct).ToListAsync(ct);
    }

    /// <inheritdoc />
    protected override Task<TUserToken> FindTokenAsync(
        TUser             user,
        string            loginProvider,
        string            name,
        CancellationToken ct) {
        return UserTokensRepository.FindAsync([user.Id, loginProvider, name], ct).AsTask();
    }

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
