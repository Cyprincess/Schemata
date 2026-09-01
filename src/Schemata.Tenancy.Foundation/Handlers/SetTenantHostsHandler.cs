using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Foundation.Commands;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Handlers;

/// <summary>Replaces a tenant's host associations with the requested, normalized host names.</summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed class SetTenantHostsHandler<TTenant>(IRepository<SchemataTenantHost> hosts)
    : IRequestHandler<SetTenantHostsRequest<TTenant>, Unit>
    where TTenant : SchemataTenant
{
    public async Task<Unit> HandleAsync(SetTenantHostsRequest<TTenant> request, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(request);

        var existing = new List<SchemataTenantHost>();
        await foreach (var row in hosts.ListAsync(q => q.Where(h => h.Tenant == request.Tenant.Name), ct)) {
            existing.Add(row);
        }

        if (existing.Count > 0) {
            await hosts.RemoveRangeAsync(existing, ct);
        }

        if (!request.Hosts.IsDefaultOrEmpty) {
            foreach (var host in request.Hosts) {
                var normalized = TenantHostNormalizer.Normalize(host);
                if (normalized is null) {
                    continue;
                }

                await hosts.AddAsync(new() { Tenant = request.Tenant.Name, Host = normalized }, ct);
            }
        }

        await hosts.CommitAsync(ct);
        return Unit.Value;
    }
}
