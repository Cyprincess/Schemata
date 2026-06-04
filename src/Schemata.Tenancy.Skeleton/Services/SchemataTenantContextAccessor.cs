using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Exceptions;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Skeleton.Services;

/// <summary>
///     Resolves and caches the current tenant for the request scope.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public class SchemataTenantContextAccessor<TTenant> : ITenantContextAccessor<TTenant>
    where TTenant : SchemataTenant
{
    private readonly ITenantManager<TTenant> _manager;
    private readonly ITenantResolver         _resolver;
    private readonly IServiceProvider        _sp;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SchemataTenantContextAccessor{TTenant}" /> class.
    /// </summary>
    public SchemataTenantContextAccessor(
        IServiceProvider        sp,
        ITenantResolver         resolver,
        ITenantManager<TTenant> manager
    ) {
        _sp       = sp;
        _resolver = resolver;
        _manager  = manager;
    }

    #region ITenantContextAccessor<TTenant> Members

    public TTenant? Tenant { get; private set; }

    public async Task InitializeAsync(CancellationToken ct) {
        var id = await _resolver.ResolveAsync(ct);

        if (id == null) {
            return;
        }

        var tenant = await _manager.FindByTenantId(id.Value, ct);
        if (tenant is null) {
            throw new TenantResolveException();
        }

        await InitializeAsync(tenant, ct);
    }

    public Task InitializeAsync(TTenant tenant, CancellationToken ct) {
        Tenant = tenant;

        return Task.CompletedTask;
    }

    public Task<IServiceProvider> GetBaseServiceProviderAsync(CancellationToken ct) { return Task.FromResult(_sp); }

    #endregion
}
