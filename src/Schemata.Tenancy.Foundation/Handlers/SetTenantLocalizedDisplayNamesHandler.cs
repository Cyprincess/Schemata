using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Foundation.Commands;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Handlers;

/// <summary>Assigns a tenant's culture-localized display names.</summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed class SetTenantLocalizedDisplayNamesHandler<TTenant>
    : IRequestHandler<SetTenantLocalizedDisplayNamesRequest<TTenant>, Unit>
    where TTenant : SchemataTenant
{
    public Task<Unit> HandleAsync(
        SetTenantLocalizedDisplayNamesRequest<TTenant> request,
        CancellationToken                               ct = default
    ) {
        ArgumentNullException.ThrowIfNull(request);

        request.Tenant.DisplayNames = request.DisplayNames is { Count: > 0 } ? request.DisplayNames : null;
        return Task.FromResult(Unit.Value);
    }
}
