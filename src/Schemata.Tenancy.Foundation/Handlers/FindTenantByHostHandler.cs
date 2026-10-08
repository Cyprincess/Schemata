using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Handlers;

/// <summary>Resolves a tenant through a normalized host-name association.</summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed class FindTenantByHostHandler<TTenant>(
    IRepository<TTenant>            tenants,
    IRepository<SchemataTenantHost> hosts
)
    where TTenant : SchemataTenant
{
    public async Task<TTenant?> HandleAsync(string host, CancellationToken ct = default) {
        using var tenantCache = tenants.SuppressQueryCache();
        using var hostCache = hosts.SuppressQueryCache();
        var normalized = TenantHostNormalizer.Normalize(host);
        if (normalized is null) {
            return null;
        }

        var match = await hosts.SingleOrDefaultAsync(q => q.Where(h => h.Host == normalized), ct);
        if (match?.Parent is null) {
            return null;
        }

        return await tenants.SingleOrDefaultAsync(q => q.Where(t => t.CanonicalName == match.Parent), ct);
    }
}
