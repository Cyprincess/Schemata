using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Handlers;

/// <summary>Persists a new tenant through the resource mutation owner.</summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
/// <typeparam name="TTenantHost">The tenant-host entity type.</typeparam>
public sealed class CreateTenantHandler<TTenant, TTenantHost>(IResourceMutation<TTenant> mutation)
    where TTenant : SchemataTenant
    where TTenantHost : SchemataTenantHost
{
    public async Task HandleAsync(TTenant tenant, CancellationToken ct = default) {
        await mutation.CreateAsync(tenant, null, ct);
    }
}
