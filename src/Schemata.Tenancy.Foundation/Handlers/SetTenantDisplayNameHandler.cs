using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Foundation.Commands;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Handlers;

/// <summary>Assigns a tenant's invariant display name.</summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed class SetTenantDisplayNameHandler<TTenant>
    : IRequestHandler<SetTenantDisplayNameRequest<TTenant>, Unit>
    where TTenant : SchemataTenant
{
    public Task<Unit> HandleAsync(SetTenantDisplayNameRequest<TTenant> request, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(request);

        request.Tenant.DisplayName = request.DisplayName;
        return Task.FromResult(Unit.Value);
    }
}
