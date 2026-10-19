using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Handlers;

/// <summary>Removes a tenant together with its host associations and evicts its cached provider after commit.</summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
/// <typeparam name="TTenantHost">The tenant-host entity type.</typeparam>
public sealed class DeleteTenantHandler<TTenant, TTenantHost>(
    IRepository<TTenant>           tenants,
    IRepository<TTenantHost>       hosts,
    IResourceMutation<TTenant>     tenantMutation,
    IResourceMutation<TTenantHost> hostMutation,
    ITenantProviderCache           cache
)
    where TTenant : SchemataTenant
    where TTenantHost : SchemataTenantHost
{
    public async Task HandleAsync(TTenant tenant, CancellationToken ct = default) {
        await using var uow = tenants.Begin();
        hosts.Join(uow);

        var existing = new List<TTenantHost>();
        await foreach (var row in hosts.ListAsync(q => q.Where(h => h.Parent == tenant.CanonicalName), ct)) {
            existing.Add(row);
        }

        foreach (var row in existing) {
            await hostMutation.DeleteAsync(row, uow, ct: ct);
        }

        var result = await tenantMutation.DeleteAsync(tenant, uow, ct: ct);
        if (result == MutationResult.Applied) {
            uow.AddCommitSink(CommitOrders.Domain, async _ => await cache.RemoveAsync(tenant.Uid.ToString()));
        }

        await uow.CommitAsync(ct);
    }
}
