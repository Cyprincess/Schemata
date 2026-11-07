using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Schemata.Entity.Repository;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Stores;

namespace Schemata.Identity.Skeleton.Managers;

/// <summary>Role manager with transaction-owning and caller-owned provisioning paths.</summary>
/// <typeparam name="TRole">The role entity type.</typeparam>
public class SchemataRoleManager<TRole>(
    IRoleStore<TRole> store,
    IEnumerable<IRoleValidator<TRole>> roleValidators,
    ILookupNormalizer keyNormalizer,
    IdentityErrorDescriber errors,
    ILogger<SchemataRoleManager<TRole>> logger
) : RoleManager<TRole>(store, roleValidators, keyNormalizer, errors, logger)
    where TRole : SchemataRole
{
    /// <summary>Creates a role and leaves an outer transaction open for its caller.</summary>
    public virtual Task<IdentityResult> CreateAsync(
        TRole role,
        IUnitOfWork? transaction,
        CancellationToken ct = default
    ) {
        return ProvisionAsync(role, transaction, true,
            (provisioning, unit) => provisioning.CreateAsync(role, unit, ct), ct);
    }

    /// <summary>Updates a role and leaves an outer transaction open for its caller.</summary>
    public virtual Task<IdentityResult> UpdateAsync(
        TRole role,
        IUnitOfWork? transaction,
        CancellationToken ct = default
    ) {
        return ProvisionAsync(role, transaction, true,
            (provisioning, unit) => provisioning.UpdateAsync(role, unit, ct), ct);
    }

    /// <summary>Deletes a role and its dependent rows and leaves an outer transaction open.</summary>
    public virtual Task<IdentityResult> DeleteAsync(
        TRole role,
        IUnitOfWork? transaction,
        CancellationToken ct = default
    ) {
        return ProvisionAsync(role, transaction, false,
            (provisioning, unit) => provisioning.DeleteAsync(role, unit, ct), ct);
    }

    private async Task<IdentityResult> ProvisionAsync(
        TRole role,
        IUnitOfWork? transaction,
        bool validate,
        Func<IRoleProvisioningStore<TRole>, IUnitOfWork, Task<IdentityResult>> operation,
        CancellationToken ct
    ) {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(role);
        var store = GetProvisioningStore();
        if (validate) {
            role.NormalizedName = NormalizeKey(role.DisplayName);
            var validation = await ValidateAsync(role);
            if (!validation.Succeeded) {
                return validation;
            }
        }

        if (transaction is not null) {
            return await operation(store, transaction);
        }

        await using var owned = store.BeginProvisioning();
        var result = await operation(store, owned);
        if (result.Succeeded) {
            await owned.CommitAsync(ct);
        }

        return result;
    }

    private async Task<IdentityResult> ValidateAsync(TRole role) {
        var errors = new List<IdentityError>();
        foreach (var validator in RoleValidators) {
            var result = await validator.ValidateAsync(this, role);
            if (!result.Succeeded) {
                errors.AddRange(result.Errors);
            }
        }

        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed(errors.ToArray());
    }

    private IRoleProvisioningStore<TRole> GetProvisioningStore() {
        if (Store is not IRoleProvisioningStore<TRole> store) {
            throw new NotSupportedException("The role store does not support transactional provisioning.");
        }

        return store;
    }
}
