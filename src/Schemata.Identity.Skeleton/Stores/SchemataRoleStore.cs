using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Skeleton.Stores;

/// <summary>
///     Repository-backed role store using the default Schemata role claim and user-role entities.
/// </summary>
/// <typeparam name="TRole">The role entity type.</typeparam>
public class SchemataRoleStore<TRole> : SchemataRoleStore<TRole, SchemataRoleClaim, SchemataUserRole>
    where TRole : SchemataRole
{
    /// <summary>
    ///     Initializes a role store with the default Schemata role claim and user-role entities.
    /// </summary>
    public SchemataRoleStore(
        IRepository<SchemataRoleClaim> roleClaims,
        IRepository<SchemataUserRole>  userRole,
        IResourceMutation<TRole>       mutation,
        IServiceProvider              services,
        IdentityErrorDescriber?        describer = null
    ) : base(roleClaims, userRole, mutation, services, describer) { }
}

/// <summary>
///     Repository-backed role store for ASP.NET Identity roles.
/// </summary>
/// <typeparam name="TRole">The role entity type.</typeparam>
/// <typeparam name="TRoleClaim">The role claim entity type.</typeparam>
/// <typeparam name="TUserRole">The user-role link entity type.</typeparam>
public class SchemataRoleStore<TRole, TRoleClaim, TUserRole> : IRoleClaimStore<TRole>
    where TRole : SchemataRole
    where TRoleClaim : SchemataRoleClaim, new()
    where TUserRole : SchemataUserRole, new()
{
    /// <summary>Repository for role claims.</summary>
    protected readonly IRepository<TRoleClaim> RoleClaimsRepository;

    /// <summary>Repository for user-role links.</summary>
    protected readonly IRepository<TUserRole> UserRoleRepository;

    private bool _disposed;

    /// <summary>
    ///     Initializes a role store with repositories for roles, role claims, and user-role links.
    /// </summary>
    public SchemataRoleStore(
        IRepository<TRoleClaim>  roleClaims,
        IRepository<TUserRole>   userRole,
        IResourceMutation<TRole> mutation,
        IServiceProvider        services,
        IdentityErrorDescriber?  describer = null
    ) {
        RoleClaimsRepository = roleClaims;
        UserRoleRepository   = userRole;
        _mutation            = mutation;
        _services            = services;
        ErrorDescriber       = describer ?? new IdentityErrorDescriber();
    }

    private readonly IResourceMutation<TRole> _mutation;
    private readonly IServiceProvider _services;

    /// <summary>Provides localized error messages for identity operations.</summary>
    public IdentityErrorDescriber ErrorDescriber { get; set; }

    #region IRoleClaimStore<TRole> Members

    public virtual async Task<IdentityResult> CreateAsync(TRole role, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (role is null) {
            throw new ArgumentNullException(nameof(role));
        }

        await _mutation.CreateAsync(role, null, ct);

        return IdentityResult.Success;
    }

    public virtual async Task<IdentityResult> UpdateAsync(TRole role, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (role is null) {
            throw new ArgumentNullException(nameof(role));
        }

        // Providers signal optimistic concurrency at different boundaries: LinqToDB throws at the
        // mutation, EF at commit. Translate both, let every other failure propagate.
        try {
            await _mutation.UpdateAsync(role, null, Operations.Update, ct);
        } catch (AbortedException) {
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

        // The owner removes the role and every dependent row (user links, claims) in one unit of
        // work so a failure cannot delete the role while leaving orphaned child rows behind.
        try {
            await _mutation.DeleteAsync(role, null, Operations.Delete, ct);
        } catch (AbortedException) {
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

        return Task.FromResult(role.DisplayName);
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

        return Task.FromResult(role.NormalizedName);
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

    public virtual async Task<IList<Claim>> GetClaimsAsync(TRole role, CancellationToken ct = default) {
        ThrowIfDisposed();
        if (role is null) {
            throw new ArgumentNullException(nameof(role));
        }

        return await RoleClaimsRepository.ListAsync(q => q.Where(rc => rc.RoleId == role.CanonicalName
                                                                    && rc.ClaimValue != null), ct)
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
                                                RoleId = role.CanonicalName!, ClaimType = claim.Type, ClaimValue = claim.Value,
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

        await using var repository = _services.GetRequiredService<IRepository<TRoleClaim>>();
        await using var unit = repository.Begin();
        Guid? cursor = null;
        while (true) {
            ct.ThrowIfCancellationRequested();
            var page = await repository.ListAsync(q => {
                var query = q.Where(rc => rc.RoleId == role.CanonicalName
                                       && rc.ClaimValue == claim.Value
                                       && rc.ClaimType == claim.Type);
                if (cursor is not null) {
                    query = query.Where(rc => rc.Uid.CompareTo(cursor.Value) > 0);
                }

                return query.OrderBy(rc => rc.Uid).Take(100);
            }, ct).ToListAsync(ct);
            if (page.Count == 0) break;

            cursor = page[^1].Uid;
            foreach (var row in page) {
                ct.ThrowIfCancellationRequested();
                await repository.RemoveAsync(row, ct);
            }
        }

        await unit.CommitAsync(ct);
    }

    #endregion

    /// <summary>
    ///     Throws when the store has been disposed.
    /// </summary>
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
        await using var roles = _services.GetRequiredService<IRepository<TRole>>();
        return await roles.SingleOrDefaultAsync(q => q.Where(r => r.Uid == roleId), ct);
    }

    public virtual async Task<TRole> FindByNameAsync(string normalizedName, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        await using var roles = _services.GetRequiredService<IRepository<TRole>>();
        return await roles.SingleOrDefaultAsync(q => q.Where(u => u.NormalizedName == normalizedName), ct);
    }
}
