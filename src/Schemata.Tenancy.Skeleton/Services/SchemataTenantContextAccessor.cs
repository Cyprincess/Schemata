using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Skeleton.Services;

public class SchemataTenantContextAccessor(
    IServiceProvider                           provider,
    ITenantResolver<Guid>                      resolver,
    ITenantManager<SchemataTenant<Guid>, Guid> manager)
    : SchemataTenantContextAccessor<SchemataTenant<Guid>, Guid>(provider, resolver, manager), ITenantContextAccessor;

public class SchemataTenantContextAccessor<TTenant, TKey>(
    IServiceProvider              provider,
    ITenantResolver<TKey>         resolver,
    ITenantManager<TTenant, TKey> manager) : ITenantContextAccessor<TTenant, TKey>
    where TTenant : SchemataTenant<TKey>
    where TKey : struct, IEquatable<TKey>
{
    #region ITenantContextAccessor<TTenant,TKey> Members

    public TTenant? Tenant { get; private set; }

    public async Task InitializeAsync(CancellationToken ct) {
        var id = await resolver.ResolveAsync(ct);

        if (id == null) {
            return;
        }

        var tenant = await manager.FindByTenantId(id.Value, ct);
        if (tenant is not { TenantId: not null }) {
            throw new InvalidOperationException("Tenant is not initialized successfully.");
        }

        await InitializeAsync(tenant, ct);
    }

    public Task InitializeAsync(TTenant tenant, CancellationToken ct) {
        Tenant = tenant;

        return Task.CompletedTask;
    }

    public Task<IServiceProvider> GetBaseServiceProviderAsync(CancellationToken ct) {
        return Task.FromResult(provider);
    }

    #endregion
}
