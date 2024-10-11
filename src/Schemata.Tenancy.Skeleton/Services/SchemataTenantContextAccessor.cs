using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Skeleton.Services;

public class SchemataTenantContextAccessor : SchemataTenantContextAccessor<SchemataTenant<Guid>, Guid>,
                                             ITenantContextAccessor
{
    public SchemataTenantContextAccessor(
        IServiceProvider                           sp,
        ITenantResolver<Guid>                      resolver,
        ITenantManager<SchemataTenant<Guid>, Guid> manager) : base(sp, resolver, manager) { }
}

public class SchemataTenantContextAccessor<TTenant, TKey> : ITenantContextAccessor<TTenant, TKey>
    where TTenant : SchemataTenant<TKey>
    where TKey : struct, IEquatable<TKey>
{
    private readonly ITenantManager<TTenant, TKey> _manager;
    private readonly ITenantResolver<TKey>         _resolver;
    private readonly IServiceProvider              _sp;

    public SchemataTenantContextAccessor(
        IServiceProvider              sp,
        ITenantResolver<TKey>         resolver,
        ITenantManager<TTenant, TKey> manager) {
        _sp       = sp;
        _resolver = resolver;
        _manager  = manager;
    }

    #region ITenantContextAccessor<TTenant,TKey> Members

    public TTenant? Tenant { get; private set; }

    public async Task InitializeAsync(CancellationToken ct) {
        var id = await _resolver.ResolveAsync(ct);

        if (id == null) {
            return;
        }

        var tenant = await _manager.FindByTenantId(id.Value, ct);
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
        return Task.FromResult(_sp);
    }

    #endregion
}
