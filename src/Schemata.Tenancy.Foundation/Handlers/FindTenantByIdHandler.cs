using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Foundation.Queries;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Handlers;

/// <summary>Finds a tenant by its unique identifier.</summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed class FindTenantByIdHandler<TTenant>(IRepository<TTenant> tenants)
    : IRequestHandler<FindTenantByIdQuery<TTenant>, TTenant?>
    where TTenant : SchemataTenant
{
    public async Task<TTenant?> HandleAsync(
        FindTenantByIdQuery<TTenant> request,
        CancellationToken            ct = default
    ) {
        ArgumentNullException.ThrowIfNull(request);

        return await tenants.SingleOrDefaultAsync(q => q.Where(t => t.Uid.Equals(request.TenantId)), ct);
    }
}
