using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Tenancy.Skeleton.Entities;
using Schemata.Tenancy.Skeleton;

namespace Schemata.Tenancy.Foundation.Handlers;

/// <summary>Replaces a tenant's host associations with the requested, normalized host names and evicts its cached provider after commit.</summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed class SetTenantHostsHandler<TTenant>(
    IRepository<TTenant>                  tenants,
    IRepository<SchemataTenantHost>       hosts,
    IResourceMutation<TTenant>            tenantMutation,
    IResourceMutation<SchemataTenantHost> hostMutation,
    ITenantProviderCache                  cache
)
    where TTenant : SchemataTenant
{
    public async Task HandleAsync(TTenant tenant, ImmutableArray<string> requested, CancellationToken ct = default) {
        await using var transaction = tenants.Begin();
        hosts.Join(transaction);

        var existing = new List<SchemataTenantHost>();
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

                await hostMutation.CreateAsync(new() { Parent = tenant.CanonicalName, Host = normalized }, transaction, ct);
            }
        }

        var result = await tenantMutation.UpdateAsync(tenant, transaction, ct: ct);
        if (result == MutationResult.Applied) {
            transaction.AddCommitSink(CommitOrders.Domain, _ => {
                cache.Remove(tenant.Uid.ToString());
                return Task.CompletedTask;
            });
        }

        await transaction.CommitAsync(ct);
    }
}
