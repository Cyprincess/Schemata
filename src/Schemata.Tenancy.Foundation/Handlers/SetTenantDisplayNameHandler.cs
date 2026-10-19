using System.Threading;
using System.Threading.Tasks;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Handlers;

/// <summary>Assigns and persists a tenant's invariant display name, evicting its cached provider after commit.</summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
/// <typeparam name="TTenantHost">The tenant-host entity type.</typeparam>
public sealed class SetTenantDisplayNameHandler<TTenant, TTenantHost>(
    UpdateTenantHandler<TTenant, TTenantHost> update
)
    where TTenant : SchemataTenant
    where TTenantHost : SchemataTenantHost
{
    public async Task HandleAsync(TTenant tenant, string? name, CancellationToken ct = default) {
        tenant.DisplayName = name;
        await update.HandleAsync(tenant, ct);
    }
}
