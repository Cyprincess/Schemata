using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Foundation.Commands;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Handlers;

/// <summary>Removes a tenant together with its host associations and evicts its cached provider.</summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed class DeleteTenantHandler<TTenant>(
    IRepository<TTenant>            tenants,
    IRepository<SchemataTenantHost> hosts,
    ITenantProviderCache            cache
) : IRequestHandler<DeleteTenantRequest<TTenant>, Unit>
    where TTenant : SchemataTenant
{
    public async Task<Unit> HandleAsync(DeleteTenantRequest<TTenant> request, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(request);

        // Remove the tenant and its hosts in one unit of work so host rows are committed
        // with the tenant removal.
        await using var uow = tenants.Begin();
        hosts.Join(uow);

        var existing = new List<SchemataTenantHost>();
        await foreach (var row in hosts.ListAsync(q => q.Where(h => h.Tenant == request.Tenant.Name), ct)) {
            existing.Add(row);
        }

        if (existing.Count > 0) {
            await hosts.RemoveRangeAsync(existing, ct);
        }

        await tenants.RemoveAsync(request.Tenant, ct);
        await uow.CommitAsync(ct);

        cache.Remove(request.Tenant.Uid.ToString());
        return Unit.Value;
    }
}
