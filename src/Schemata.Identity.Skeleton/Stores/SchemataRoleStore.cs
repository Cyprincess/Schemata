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

public class SchemataRoleStore<TRole> : SchemataRoleStore<TRole, SchemataRoleClaim, SchemataUserRole>
    where TRole : SchemataRole
{
    public SchemataRoleStore(
        IRepository<TRole>             roles,
        IRepository<SchemataRoleClaim> roleClaims,
        IRepository<SchemataUserRole>  userRole,
        IdentityErrorDescriber?        describer = null
    ) : base(roles, roleClaims, userRole, describer) { }
}

public class SchemataRoleStore<TRole, TRoleClaim, TUserRole> : IQueryableRoleStore<TRole>, IRoleClaimStore<TRole>
    where TRole : SchemataRole
    where TRoleClaim : SchemataRoleClaim, new()
    where TUserRole : SchemataUserRole, new()
{
    protected readonly IRepository<TRoleClaim> RoleClaimsRepository;

    protected readonly IRepository<TRole> RolesRepository;

    protected readonly IRepository<TUserRole> UserRoleRepository;

    private bool _disposed;

    public SchemataRoleStore(
        IRepository<TRole>      roles,
        IRepository<TRoleClaim> roleClaims,
        IRepository<TUserRole>  userRole,
        IdentityErrorDescriber? describer = null
    ) {
        RoleClaimsRepository = roleClaims;
        RolesRepository      = roles;
        UserRoleRepository   = userRole;
        ErrorDescriber       = describer ?? new IdentityErrorDescriber();
    }

    /// <summary>Provides localized error messages for identity operations.</summary>
    public IdentityErrorDescriber ErrorDescriber { get; set; }

    #region IQueryableRoleStore<TRole> Members

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

    public virtual async Task<IdentityResult> DeleteAsync(TRole role, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (role is null) {
            throw new ArgumentNullException(nameof(role));
        }

        await foreach (var user in UserRoleRepository.ListAsync(q => q.Where(ur => ur.RoleId.Equals(role.Uid)))
                                                     .WithCancellation(ct)) {
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

    public virtual Task<string> GetRoleIdAsync(TRole role, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (role is null) {
            throw new ArgumentNullException(nameof(role));
        }

        return Task.FromResult(role.Uid.ToString());
    }

    public virtual Task<string?> GetRoleNameAsync(TRole role, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (role is null) {
            throw new ArgumentNullException(nameof(role));
        }

        return Task.FromResult<string?>(role.DisplayName);
    }

    public virtual Task SetRoleNameAsync(TRole role, string? roleName, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (role is null) {
            throw new ArgumentNullException(nameof(role));
        }

        role.DisplayName = roleName;
        return Task.CompletedTask;
    }

    public virtual Task<string?> GetNormalizedRoleNameAsync(TRole role, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (role is null) {
            throw new ArgumentNullException(nameof(role));
        }

        return Task.FromResult<string?>(role.NormalizedName);
    }

    public virtual Task SetNormalizedRoleNameAsync(TRole role, string? normalizedName, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (role is null) {
            throw new ArgumentNullException(nameof(role));
        }

        role.NormalizedName = normalizedName;
        return Task.CompletedTask;
    }

    public virtual void Dispose() { _disposed = true; }

    public virtual IQueryable<TRole> Roles => RolesRepository.AsQueryable();

    #endregion

    #region IRoleClaimStore<TRole> Members

    public virtual async Task<IList<Claim>> GetClaimsAsync(TRole role, CancellationToken ct = default) {
        ThrowIfDisposed();
        if (role is null) {
            throw new ArgumentNullException(nameof(role));
        }

        return await RoleClaimsRepository.ListAsync(q => q.Where(rc => rc.RoleId.Equals(role.Uid)), ct)
                                         .Map(c => new Claim(c.ClaimType!, c.ClaimValue!), ct)
                                         .ToListAsync(ct);
    }

    public virtual async Task AddClaimAsync(TRole role, Claim claim, CancellationToken ct = default) {
        ThrowIfDisposed();
        if (role is null) {
            throw new ArgumentNullException(nameof(role));
        }

        if (claim is null) {
            throw new ArgumentNullException(nameof(claim));
        }

        await RoleClaimsRepository.AddAsync(new() {
                                                RoleId = role.Uid, ClaimType = claim.Type, ClaimValue = claim.Value,
                                            }, ct);
        await RoleClaimsRepository.CommitAsync(ct);
    }

    public virtual async Task RemoveClaimAsync(TRole role, Claim claim, CancellationToken ct = default) {
        ThrowIfDisposed();
        if (role is null) {
            throw new ArgumentNullException(nameof(role));
        }

        if (claim is null) {
            throw new ArgumentNullException(nameof(claim));
        }

        await foreach (var c in RoleClaimsRepository
                               .ListAsync(q => q.Where(rc => rc.RoleId.Equals(role.Uid)
                                                          && rc.ClaimValue == claim.Value
                                                          && rc.ClaimType == claim.Type))
                               .WithCancellation(ct)) {
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

#nullable disable
    public virtual async Task<TRole> FindByIdAsync(string id, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        var roleId = Guid.Parse(id);
        return await RolesRepository.SingleOrDefaultAsync(q => q.Where(r => r.Uid == roleId), ct);
    }

    public virtual async Task<TRole> FindByNameAsync(string normalizedName, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        return await RolesRepository.SingleOrDefaultAsync(q => q.Where(u => u.NormalizedName == normalizedName), ct);
    }
}
