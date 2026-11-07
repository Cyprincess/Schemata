using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Scim.Entities;

namespace Schemata.Scim.Services;

/// <summary>Repository-backed provisioning binding owner.</summary>
public sealed class ScimBindingStore(IRepository<ScimProvisioningBinding> repository) : IScimBindingStore
{
    public Task<ScimProvisioningBinding?> FindByExternalIdAsync(
        string resourceType, string provisioningDomain, string externalId, CancellationToken ct
    ) {
        return repository.SingleOrDefaultAsync(q => q.Where(binding => binding.ResourceType == resourceType
                                                                   && binding.ProvisioningDomain == provisioningDomain
                                                                   && binding.ExternalId == externalId), ct)
                       .AsTask();
    }

    public Task<ScimProvisioningBinding?> FindByEntityUidAsync(
        string resourceType, Guid entityUid, string provisioningDomain, CancellationToken ct
    ) {
        return repository.SingleOrDefaultAsync(q => q.Where(binding => binding.ResourceType == resourceType
                                                                   && binding.EntityUid == entityUid
                                                                   && binding.ProvisioningDomain == provisioningDomain), ct)
                       .AsTask();
    }

    public async Task<IReadOnlyList<ScimProvisioningBinding>> ListByEntityUidAsync(
        string resourceType, Guid entityUid, CancellationToken ct
    ) {
        return await repository.ListAsync(q => q.Where(binding => binding.ResourceType == resourceType
                                                               && binding.EntityUid == entityUid), ct)
                               .ToListAsync(ct);
    }

    public async Task StageAsync(ScimProvisioningBinding binding, IUnitOfWork transaction, CancellationToken ct) {
        repository.Join(transaction);
        if (await repository.AddAsync(binding, ct) != MutationResult.Applied) {
            throw new InvalidOperationException("The SCIM provisioning binding was not staged.");
        }
    }

    public async Task UpdateAsync(ScimProvisioningBinding binding, IUnitOfWork transaction, CancellationToken ct) {
        repository.Join(transaction);
        if (await repository.UpdateAsync(binding, ct) != MutationResult.Applied) {
            throw new InvalidOperationException("The SCIM provisioning binding update was not staged.");
        }
    }

    public async Task RemoveAsync(ScimProvisioningBinding binding, IUnitOfWork transaction, CancellationToken ct) {
        repository.Join(transaction);
        if (await repository.RemoveAsync(binding, ct) != MutationResult.Applied) {
            throw new InvalidOperationException("The SCIM provisioning binding removal was not staged.");
        }
    }
}
