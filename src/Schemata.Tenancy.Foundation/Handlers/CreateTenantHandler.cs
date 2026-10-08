using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Handlers;

/// <summary>Persists a new tenant through the resource mutation owner.</summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed class CreateTenantHandler<TTenant>(IResourceMutation<TTenant> mutation)
    where TTenant : SchemataTenant
{
    public async Task HandleAsync(TTenant tenant, CancellationToken ct = default) {
        await mutation.CreateAsync(tenant, null, ct);
    }
}
