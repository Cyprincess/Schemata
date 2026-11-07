using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Schemata.Entity.Repository;

namespace Schemata.Identity.Skeleton.Stores;

/// <summary>Stages role provisioning through a caller-owned or store-owned transaction.</summary>
/// <typeparam name="TRole">The role entity type.</typeparam>
public interface IRoleProvisioningStore<TRole>
{
    IUnitOfWork BeginProvisioning();

    Task<IdentityResult> CreateAsync(TRole role, IUnitOfWork transaction, CancellationToken ct);

    Task<IdentityResult> UpdateAsync(TRole role, IUnitOfWork transaction, CancellationToken ct);

    Task<IdentityResult> DeleteAsync(TRole role, IUnitOfWork transaction, CancellationToken ct);
}
