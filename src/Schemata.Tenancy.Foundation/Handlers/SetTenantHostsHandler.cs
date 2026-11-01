using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Handlers;

/// <summary>Replaces a tenant's host associations with the requested, normalized host names and evicts its cached provider after commit.</summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
/// <typeparam name="TTenantHost">The tenant-host entity type.</typeparam>
public sealed class SetTenantHostsHandler<TTenant, TTenantHost>(
    IRepository<TTenant>                  tenants,
    IRepository<TTenantHost>              hosts,
    IResourceMutation<TTenant>            tenantMutation,
    IResourceMutation<TTenantHost>        hostMutation,
    ITenantProviderCache                  cache
)
    where TTenant : SchemataTenant
    where TTenantHost : SchemataTenantHost, new()
{
    public async Task HandleAsync(TTenant tenant, ImmutableArray<string> requested, CancellationToken ct = default) {
        await using var transaction = tenants.Begin();
        hosts.Join(transaction);

        var existing = new List<TTenantHost>();
        await foreach (var row in hosts.ListAsync(q => q.Where(h => h.Parent == tenant.CanonicalName), ct)) {
            existing.Add(row);
        }

        foreach (var row in existing) {
            await hostMutation.DeleteAsync(row, transaction, ct: ct);
        }

        if (!requested.IsDefaultOrEmpty) {
            foreach (var host in requested) {
                var normalized = TenantHostNormalizer.Normalize(host);
                if (normalized is null) {
                    continue;
                }

                await hostMutation.CreateAsync(new TTenantHost { Parent = tenant.CanonicalName, Host = normalized }, transaction, ct);
            }
        }

        var result = await tenantMutation.UpdateAsync(tenant, transaction, ct: ct);
        if (result == MutationResult.Applied) {
            transaction.AddCommitSink(CommitOrders.Domain, async _ => await cache.RemoveAsync(tenant.Uid.ToString()));
        }

        await transaction.CommitAsync(ct);
    }
}
