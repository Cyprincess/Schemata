using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Schemata.Entity.Repository;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Skeleton.Stores;

/// <summary>Stages user provisioning through a caller-owned or store-owned transaction.</summary>
/// <typeparam name="TUser">The user entity type.</typeparam>
public interface IUserProvisioningStore<TUser>
{
    IUnitOfWork BeginProvisioning();

    Task<IdentityResult> CreateAsync(TUser user, IUnitOfWork transaction, CancellationToken ct);

    Task<IdentityResult> UpdateAsync(TUser user, IUnitOfWork transaction, CancellationToken ct);

    Task<IdentityResult> DeleteAsync(TUser user, IUnitOfWork transaction, CancellationToken ct);

    Task AddToRoleAsync(TUser user, SchemataRole role, IUnitOfWork transaction, CancellationToken ct);

    Task RemoveFromRoleAsync(TUser user, SchemataRole role, IUnitOfWork transaction, CancellationToken ct);
}
