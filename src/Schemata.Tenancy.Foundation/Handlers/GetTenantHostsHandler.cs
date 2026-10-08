using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Handlers;

/// <summary>Lists a tenant's stored host names in association order.</summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed class GetTenantHostsHandler<TTenant>(IRepository<SchemataTenantHost> hosts)
    where TTenant : SchemataTenant
{
    public async Task<ImmutableArray<string>> HandleAsync(TTenant tenant, CancellationToken ct = default) {
        var builder = ImmutableArray.CreateBuilder<string>();

        await foreach (var row in hosts.ListAsync(q => q.Where(h => h.Parent == tenant.CanonicalName), ct)) {
            if (!string.IsNullOrWhiteSpace(row.Host)) {
                builder.Add(row.Host!);
            }
        }

        return builder.ToImmutable();
    }
}
