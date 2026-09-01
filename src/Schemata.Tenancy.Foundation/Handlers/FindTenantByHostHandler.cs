using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Foundation.Queries;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Handlers;

/// <summary>Resolves a tenant through a normalized host-name association.</summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed class FindTenantByHostHandler<TTenant>(
    IRepository<TTenant>            tenants,
    IRepository<SchemataTenantHost> hosts
) : IRequestHandler<FindTenantByHostQuery<TTenant>, TTenant?>
    where TTenant : SchemataTenant
{
    public async Task<TTenant?> HandleAsync(FindTenantByHostQuery<TTenant> request, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(request);

        var normalized = TenantHostNormalizer.Normalize(request.Host);
        if (normalized is null) {
            return null;
        }

        var match = await hosts.SingleOrDefaultAsync(q => q.Where(h => h.Host == normalized), ct);
        if (match?.Tenant is null) {
            return null;
        }

        return await tenants.SingleOrDefaultAsync(q => q.Where(t => t.Name == match.Tenant), ct);
    }
}
