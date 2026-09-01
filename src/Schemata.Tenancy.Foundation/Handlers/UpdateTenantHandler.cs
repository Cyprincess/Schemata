using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Foundation.Commands;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Handlers;

/// <summary>Persists changes to an existing tenant and evicts its cached provider.</summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed class UpdateTenantHandler<TTenant>(IRepository<TTenant> tenants, ITenantProviderCache cache)
    : IRequestHandler<UpdateTenantRequest<TTenant>, Unit>
    where TTenant : SchemataTenant
{
    public async Task<Unit> HandleAsync(UpdateTenantRequest<TTenant> request, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(request);

        await tenants.UpdateAsync(request.Tenant, ct);
        await tenants.CommitAsync(ct);

        cache.Remove(request.Tenant.Uid.ToString());
        return Unit.Value;
    }
}
