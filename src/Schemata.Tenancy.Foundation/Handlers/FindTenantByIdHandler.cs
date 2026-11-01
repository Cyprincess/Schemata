using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Handlers;

/// <summary>Finds a tenant by its unique identifier.</summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
/// <typeparam name="TTenantHost">The tenant-host entity type.</typeparam>
public sealed class FindTenantByIdHandler<TTenant, TTenantHost>(IRepository<TTenant> tenants)
    where TTenant : SchemataTenant
    where TTenantHost : SchemataTenantHost
{
    public async Task<TTenant?> HandleAsync(Guid identifier, CancellationToken ct = default) {
        return await tenants.SingleOrDefaultAsync(
            q => q.Where(t => t.Uid == identifier), ct, new QueryOptions(IgnoreCache: true));
    }
}
