using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.Repository;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Handlers;

/// <summary>Assigns and persists a tenant's culture-localized display names, evicting its cached provider after commit.</summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed class SetTenantLocalizedDisplayNamesHandler<TTenant>(
    IServiceProvider          services,
    IResourceMutation<TTenant> mutation,
    ITenantProviderCache       cache
)
    where TTenant : SchemataTenant
{
    public async Task HandleAsync(TTenant tenant, Dictionary<string, string?> names, CancellationToken ct = default) {
        tenant.DisplayNames = names.Count > 0 ? names : null;
        await using var tenants = services.GetRequiredService<IRepository<TTenant>>();
        await using var uow = tenants.Begin();
        var result = await mutation.UpdateAsync(tenant, uow, ct: ct);
        if (result == MutationResult.Applied) {
            uow.AddCommitSink(CommitOrders.Domain, _ => {
                cache.Remove(tenant.Uid.ToString());
                return Task.CompletedTask;
            });
        }

        await uow.CommitAsync(ct);
    }
}
