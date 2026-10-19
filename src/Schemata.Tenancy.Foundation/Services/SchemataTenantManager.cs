using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Tenancy.Foundation.Handlers;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Services;

public class SchemataTenantManager<TTenant, TTenantHost>(IServiceProvider services) : ITenantManager<TTenant>
    where TTenant : SchemataTenant
    where TTenantHost : SchemataTenantHost, new()
{
    public virtual ValueTask<TTenant?> FindByTenantId(Guid identifier, CancellationToken ct) =>
        new(services.GetRequiredService<FindTenantByIdHandler<TTenant, TTenantHost>>().HandleAsync(identifier, ct));

    public virtual ValueTask<TTenant?> FindByHost(string host, CancellationToken ct) =>
        new(services.GetRequiredService<FindTenantByHostHandler<TTenant, TTenantHost>>().HandleAsync(host, ct));

    public virtual ValueTask<ImmutableArray<string>> GetHostsAsync(TTenant tenant, CancellationToken ct) =>
        new(services.GetRequiredService<GetTenantHostsHandler<TTenant, TTenantHost>>().HandleAsync(tenant, ct));

    public virtual ValueTask SetDisplayNameAsync(TTenant tenant, string? name, CancellationToken ct) =>
        new(services.GetRequiredService<SetTenantDisplayNameHandler<TTenant, TTenantHost>>().HandleAsync(tenant, name, ct));

    public virtual ValueTask SetDisplayNamesAsync(TTenant tenant, Dictionary<string, string?> names, CancellationToken ct) =>
        new(services.GetRequiredService<SetTenantLocalizedDisplayNamesHandler<TTenant, TTenantHost>>().HandleAsync(tenant, names, ct));

    public virtual ValueTask SetHostsAsync(TTenant tenant, ImmutableArray<string> hosts, CancellationToken ct) =>
        new(services.GetRequiredService<SetTenantHostsHandler<TTenant, TTenantHost>>().HandleAsync(tenant, hosts, ct));

    public virtual ValueTask CreateAsync(TTenant tenant, CancellationToken ct) =>
        new(services.GetRequiredService<CreateTenantHandler<TTenant, TTenantHost>>().HandleAsync(tenant, ct));

    public virtual ValueTask DeleteAsync(TTenant tenant, CancellationToken ct) =>
        new(services.GetRequiredService<DeleteTenantHandler<TTenant, TTenantHost>>().HandleAsync(tenant, ct));

    public virtual ValueTask UpdateAsync(TTenant tenant, CancellationToken ct) =>
        new(services.GetRequiredService<UpdateTenantHandler<TTenant, TTenantHost>>().HandleAsync(tenant, ct));
}
