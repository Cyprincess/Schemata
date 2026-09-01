using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Foundation.Queries;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Handlers;

/// <summary>Lists a tenant's stored host names in association order.</summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed class GetTenantHostsHandler<TTenant>(IRepository<SchemataTenantHost> hosts)
    : IRequestHandler<GetTenantHostsQuery<TTenant>, ImmutableArray<string>>
    where TTenant : SchemataTenant
{
    public async Task<ImmutableArray<string>> HandleAsync(
        GetTenantHostsQuery<TTenant> request,
        CancellationToken             ct = default
    ) {
        ArgumentNullException.ThrowIfNull(request);

        var builder = ImmutableArray.CreateBuilder<string>();

        await foreach (var row in hosts.ListAsync(q => q.Where(h => h.Tenant == request.Tenant.Name), ct)) {
            if (!string.IsNullOrWhiteSpace(row.Host)) {
                builder.Add(row.Host!);
            }
        }

        return builder.ToImmutable();
    }
}
