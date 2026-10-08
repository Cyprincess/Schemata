using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Exceptions;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Services;

/// <summary>
///     Resolves and caches the current tenant for the request scope.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public class SchemataTenantContextAccessor<TTenant> : ITenantContextAccessor<TTenant>, ITenantContextInitializer<TTenant>
    where TTenant : SchemataTenant
{
    private readonly ITenantManager<TTenant> _manager;
    private readonly IEnumerable<ITenantResolver> _resolvers;
    private readonly IServiceProvider        _sp;

    /// <summary>Creates an accessor that resolves tenants through the registered resolver and manager.</summary>
    public SchemataTenantContextAccessor(
        IServiceProvider        sp,
        IEnumerable<ITenantResolver> resolvers,
        ITenantManager<TTenant> manager
    ) {
        _sp       = sp;
        _resolvers = resolvers;
        _manager  = manager;
    }

    #region ITenantContextAccessor<TTenant> Members

    public TTenant? Tenant { get; private set; }

    public Task InitializeAsync(CancellationToken ct) => InitializeAsync(TenantResolutionStage.Request, ct);

    public async Task InitializeAsync(TenantResolutionStage stage, CancellationToken ct) {
        Guid? selected = Tenant?.Uid;
        foreach (var resolver in _resolvers) {
            if (resolver.Stage != stage) continue;
            var id = await resolver.ResolveAsync(ct);
            if (id is null) continue;
            if (selected is { } current && current != id) throw new TenantResolveException();
            var tenant = await _manager.FindByTenantId(id.Value, ct);
            if (tenant is null) throw new TenantResolveException();
            selected = id;
            Tenant = tenant;
        }
    }

    public Task InitializeAsync(TTenant tenant, CancellationToken ct) {
        Tenant = tenant;

        return Task.CompletedTask;
    }

    public Task<IServiceProvider> GetBaseServiceProviderAsync(CancellationToken ct) { return Task.FromResult(_sp); }

    #endregion
}
