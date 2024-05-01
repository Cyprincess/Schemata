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

public class SchemataRoleStore<TRole>(
    IRepository<TRole>             roles,
    IRepository<SchemataRoleClaim> roleClaims,
    IRepository<SchemataUserRole>  userRole,
    IdentityErrorDescriber?        describer = null) : SchemataRoleStore<TRole, SchemataRoleClaim, SchemataUserRole>(roles, roleClaims, userRole, describer)
    where TRole : SchemataRole;

public class SchemataRoleStore<TRole, TRoleClaim, TUserRole>(
    IRepository<TRole>      roles,
    IRepository<TRoleClaim> roleClaims,
    IRepository<TUserRole>  userRole,
    IdentityErrorDescriber? describer = null) : IQueryableRoleStore<TRole>, IRoleClaimStore<TRole>
    where TRole : SchemataRole
    where TRoleClaim : SchemataRoleClaim, new()
    where TUserRole : SchemataUserRole, new()
{
    protected readonly IRepository<TRoleClaim> RoleClaimsRepository = roleClaims;
    protected readonly IRepository<TRole>      RolesRepository      = roles;
    protected readonly IRepository<TUserRole>  UserRoleRepository   = userRole;

    private bool _disposed;

    public IdentityErrorDescriber ErrorDescriber { get; set; } = describer ?? new IdentityErrorDescriber();

    #region IQueryableRoleStore<TRole> Members

    /// <inheritdoc />
    public virtual async Task<IdentityResult> CreateAsync(TRole role, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (role is null) {
            throw new ArgumentNullException(nameof(role));
        }

        await RolesRepository.AddAsync(role, ct);
        await RolesRepository.CommitAsync(ct);
        return IdentityResult.Success;
    }

    /// <inheritdoc />
    public virtual async Task<IdentityResult> UpdateAsync(TRole role, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (role is null) {
            throw new ArgumentNullException(nameof(role));
        }

        await RolesRepository.UpdateAsync(role, ct);
        try {
            await RolesRepository.CommitAsync(ct);
        } catch (ConcurrencyException) {
            return IdentityResult.Failed(ErrorDescriber.ConcurrencyFailure());
        }

        return IdentityResult.Success;
    }

    /// <inheritdoc />
    public virtual async Task<IdentityResult> DeleteAsync(TRole role, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (role is null) {
            throw new ArgumentNullException(nameof(role));
        }

        var users = UserRoleRepository.ListAsync(q => q.Where(ur => ur.RoleId.Equals(role.Id)), ct);
        await foreach (var user in users) {
            await UserRoleRepository.RemoveAsync(user, ct);
        }

        await UserRoleRepository.CommitAsync(ct);

        await RolesRepository.RemoveAsync(role, ct);
        try {
            await RolesRepository.CommitAsync(ct);
        } catch (ConcurrencyException) {
            return IdentityResult.Failed(ErrorDescriber.ConcurrencyFailure());
        }

        return IdentityResult.Success;
    }

    /// <inheritdoc />
    public virtual Task<string> GetRoleIdAsync(TRole role, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (role is null) {
            throw new ArgumentNullException(nameof(role));
        }

        return Task.FromResult(role.Id.ToString());
    }

    /// <inheritdoc />
    public virtual Task<string?> GetRoleNameAsync(TRole role, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (role is null) {
            throw new ArgumentNullException(nameof(role));
        }

        return Task.FromResult<string?>(role.DisplayName);
    }

    /// <inheritdoc />
    public virtual Task SetRoleNameAsync(TRole role, string? roleName, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (role is null) {
            throw new ArgumentNullException(nameof(role));
        }

        role.DisplayName = roleName;
        return Task.CompletedTask;
    }

#nullable disable
    /// <inheritdoc />
    public virtual async Task<TRole> FindByIdAsync(string id, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        var roleId = long.Parse(id);
        return await RolesRepository.SingleOrDefaultAsync(q => q.Where(u => u.Id == roleId), ct);
    }
#nullable restore

#nullable disable
    /// <inheritdoc />
    public virtual async Task<TRole> FindByNameAsync(string normalizedName, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        return await RolesRepository.SingleOrDefaultAsync(q => q.Where(u => u.NormalizedName == normalizedName), ct);
    }
#nullable restore

    /// <inheritdoc />
    public virtual Task<string?> GetNormalizedRoleNameAsync(TRole role, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (role is null) {
            throw new ArgumentNullException(nameof(role));
        }

        return Task.FromResult<string?>(role.NormalizedName);
    }

    /// <inheritdoc />
    public virtual Task SetNormalizedRoleNameAsync(TRole role, string? normalizedName, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (role is null) {
            throw new ArgumentNullException(nameof(role));
        }

        role.NormalizedName = normalizedName;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual void Dispose() {
        _disposed = true;
    }

    /// <inheritdoc />
    public virtual IQueryable<TRole> Roles => RolesRepository.AsQueryable();

    #endregion

    #region IRoleClaimStore<TRole> Members

    /// <inheritdoc />
    public virtual async Task<IList<Claim>> GetClaimsAsync(TRole role, CancellationToken ct = default) {
        ThrowIfDisposed();
        if (role is null) {
            throw new ArgumentNullException(nameof(role));
        }

        return await RoleClaimsRepository.ListAsync(q => q.Where(rc => rc.RoleId.Equals(role.Id)), ct)
                                         .Map(c => new Claim(c.ClaimType!, c.ClaimValue!), ct)
                                         .ToListAsync(ct);
    }

    /// <inheritdoc />
    public virtual async Task AddClaimAsync(TRole role, Claim claim, CancellationToken ct = default) {
        ThrowIfDisposed();
        if (role is null) {
            throw new ArgumentNullException(nameof(role));
        }

        if (claim is null) {
            throw new ArgumentNullException(nameof(claim));
        }

        await RoleClaimsRepository.AddAsync(new() {
            RoleId     = role.Id,
            ClaimType  = claim.Type,
            ClaimValue = claim.Value,
        }, ct);
        await RoleClaimsRepository.CommitAsync(ct);
    }

    /// <inheritdoc />
    public virtual async Task RemoveClaimAsync(TRole role, Claim claim, CancellationToken ct = default) {
        ThrowIfDisposed();
        if (role is null) {
            throw new ArgumentNullException(nameof(role));
        }

        if (claim is null) {
            throw new ArgumentNullException(nameof(claim));
        }

        var claims = RoleClaimsRepository.ListAsync(q => q.Where(rc => rc.RoleId.Equals(role.Id) && rc.ClaimValue == claim.Value && rc.ClaimType == claim.Type), ct);
        await foreach (var c in claims) {
            await RoleClaimsRepository.RemoveAsync(c, ct);
        }

        await RoleClaimsRepository.CommitAsync(ct);
    }

    #endregion

    protected virtual void ThrowIfDisposed() {
        if (!_disposed) {
            return;
        }

        throw new ObjectDisposedException(GetType().Name);
    }
}
