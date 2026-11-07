using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Scim.Entities;

namespace Schemata.Scim.Services;

/// <summary>Reads and stages provisioning bindings without owning transaction completion.</summary>
public interface IScimBindingStore
{
    Task<ScimProvisioningBinding?> FindByExternalIdAsync(
        string resourceType, string provisioningDomain, string externalId, CancellationToken ct);

    Task<ScimProvisioningBinding?> FindByEntityUidAsync(
        string resourceType, Guid entityUid, string provisioningDomain, CancellationToken ct);

    Task<IReadOnlyList<ScimProvisioningBinding>> ListByEntityUidAsync(
        string resourceType, Guid entityUid, CancellationToken ct);

    Task StageAsync(ScimProvisioningBinding binding, IUnitOfWork transaction, CancellationToken ct);

    Task UpdateAsync(ScimProvisioningBinding binding, IUnitOfWork transaction, CancellationToken ct);

    Task RemoveAsync(ScimProvisioningBinding binding, IUnitOfWork transaction, CancellationToken ct);
}
