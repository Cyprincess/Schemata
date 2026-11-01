using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Handlers;

/// <summary>Resolves a tenant through a normalized host-name association.</summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
/// <typeparam name="TTenantHost">The tenant-host entity type.</typeparam>
public sealed class FindTenantByHostHandler<TTenant, TTenantHost>(
    IRepository<TTenant>     tenants,
    IRepository<TTenantHost> hosts
)
    where TTenant : SchemataTenant
    where TTenantHost : SchemataTenantHost
{
    public async Task<TTenant?> HandleAsync(string host, CancellationToken ct = default) {
        var normalized = TenantHostNormalizer.Normalize(host);
        if (normalized is null) {
            return null;
        }

        var match = await hosts.SingleOrDefaultAsync(
            q => q.Where(h => h.Host == normalized), ct, new QueryOptions(IgnoreCache: true));
        if (match?.Parent is null) {
            return null;
        }

        return await tenants.SingleOrDefaultAsync(
            q => q.Where(t => t.CanonicalName == match.Parent), ct, new QueryOptions(IgnoreCache: true));
    }
}
