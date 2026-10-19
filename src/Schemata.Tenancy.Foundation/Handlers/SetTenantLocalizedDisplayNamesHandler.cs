using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Handlers;

/// <summary>Assigns and persists a tenant's culture-localized display names, evicting its cached provider after commit.</summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
/// <typeparam name="TTenantHost">The tenant-host entity type.</typeparam>
public sealed class SetTenantLocalizedDisplayNamesHandler<TTenant, TTenantHost>(
    UpdateTenantHandler<TTenant, TTenantHost> update
)
    where TTenant : SchemataTenant
    where TTenantHost : SchemataTenantHost
{
    public async Task HandleAsync(TTenant tenant, Dictionary<string, string?> names, CancellationToken ct = default) {
        tenant.DisplayNames = names.Count > 0 ? names : null;
        await update.HandleAsync(tenant, ct);
    }
}
