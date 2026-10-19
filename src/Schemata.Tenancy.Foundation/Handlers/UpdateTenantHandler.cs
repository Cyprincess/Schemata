using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.Repository;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Handlers;

/// <summary>Persists changes to an existing tenant and evicts its cached provider after commit.</summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
/// <typeparam name="TTenantHost">The tenant-host entity type.</typeparam>
public sealed class UpdateTenantHandler<TTenant, TTenantHost>(
    IServiceProvider           services,
    IResourceMutation<TTenant> mutation,
    ITenantProviderCache       cache
)
    where TTenant : SchemataTenant
    where TTenantHost : SchemataTenantHost
{
    public async Task HandleAsync(TTenant tenant, CancellationToken ct = default) {
        await using var tenants = services.GetRequiredService<IRepository<TTenant>>();
        await using var uow = tenants.Begin();
        var result = await mutation.UpdateAsync(tenant, uow, ct: ct);
        if (result == MutationResult.Applied) {
            uow.AddCommitSink(CommitOrders.Domain, async _ => await cache.RemoveAsync(tenant.Uid.ToString()));
        }

        await uow.CommitAsync(ct);
    }
}
