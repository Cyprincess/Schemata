using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Foundation.Commands;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Handlers;

/// <summary>Persists a new tenant and commits the change.</summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed class CreateTenantHandler<TTenant>(IRepository<TTenant> tenants)
    : IRequestHandler<CreateTenantRequest<TTenant>, Unit>
    where TTenant : SchemataTenant
{
    public async Task<Unit> HandleAsync(CreateTenantRequest<TTenant> request, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(request);

        await tenants.AddAsync(request.Tenant, ct);
        await tenants.CommitAsync(ct);
        return Unit.Value;
    }
}
