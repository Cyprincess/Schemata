using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Handlers;

/// <summary>Finds a tenant by its unique identifier.</summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed class FindTenantByIdHandler<TTenant>(IRepository<TTenant> tenants)
    where TTenant : SchemataTenant
{
    public async Task<TTenant?> HandleAsync(Guid identifier, CancellationToken ct = default) {
        using var uncached = tenants.SuppressQueryCache();
        return await tenants.SingleOrDefaultAsync(q => q.Where(t => t.Uid == identifier), ct);
    }
}
