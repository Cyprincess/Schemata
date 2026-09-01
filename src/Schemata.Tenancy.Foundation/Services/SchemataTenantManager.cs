using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Foundation.Commands;
using Schemata.Tenancy.Foundation.Queries;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Services;

/// <summary>
///     Dispatcher-backed implementation of <see cref="ITenantManager{TTenant}" />.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public class SchemataTenantManager<TTenant>(IRequestDispatcher dispatcher) : ITenantManager<TTenant>
    where TTenant : SchemataTenant
{
    public virtual ValueTask<TTenant?> FindByTenantId(Guid identifier, CancellationToken ct) {
        return new(dispatcher.SendAsync<FindTenantByIdQuery<TTenant>, TTenant?>(
            new(identifier), ct));
    }

    public virtual ValueTask<TTenant?> FindByHost(string host, CancellationToken ct) {
        return new(dispatcher.SendAsync<FindTenantByHostQuery<TTenant>, TTenant?>(
            new(host), ct));
    }

    public virtual ValueTask<ImmutableArray<string>> GetHostsAsync(TTenant tenant, CancellationToken ct) {
        return new(dispatcher.SendAsync<GetTenantHostsQuery<TTenant>, ImmutableArray<string>>(
            new(tenant), ct));
    }

    public virtual ValueTask SetDisplayNameAsync(TTenant tenant, string? name, CancellationToken ct) {
        return new(dispatcher.SendAsync<SetTenantDisplayNameRequest<TTenant>, Unit>(
            new(tenant, name), ct));
    }

    public virtual ValueTask SetDisplayNamesAsync(
        TTenant                     tenant,
        Dictionary<string, string?> names,
        CancellationToken           ct
    ) {
        return new(dispatcher.SendAsync<SetTenantLocalizedDisplayNamesRequest<TTenant>, Unit>(
            new(tenant, names), ct));
    }

    public virtual ValueTask SetHostsAsync(TTenant tenant, ImmutableArray<string> hosts, CancellationToken ct) {
        return new(dispatcher.SendAsync<SetTenantHostsRequest<TTenant>, Unit>(
            new(tenant, hosts), ct));
    }

    public virtual ValueTask CreateAsync(TTenant tenant, CancellationToken ct) {
        return new(dispatcher.SendAsync<CreateTenantRequest<TTenant>, Unit>(
            new(tenant), ct));
    }

    public virtual ValueTask DeleteAsync(TTenant tenant, CancellationToken ct) {
        return new(dispatcher.SendAsync<DeleteTenantRequest<TTenant>, Unit>(
            new(tenant), ct));
    }

    public virtual ValueTask UpdateAsync(TTenant tenant, CancellationToken ct) {
        return new(dispatcher.SendAsync<UpdateTenantRequest<TTenant>, Unit>(
            new(tenant), ct));
    }
}
