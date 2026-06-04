using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Skeleton.Services;

/// <summary>
///     <see cref="ITenantContextAccessor{TTenant}" /> implementation used inside per-tenant
///     service providers. The tenant value is bound at construction; HTTP-based resolution
///     is skipped because the tenant has already been resolved by the request pipeline before
///     the tenant scope was created.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed class TenantBoundContextAccessor<TTenant> : ITenantContextAccessor<TTenant>
    where TTenant : SchemataTenant
{
    private readonly IServiceProvider _root;

    /// <summary>Initializes a new instance bound to the specified tenant and root service provider.</summary>
    public TenantBoundContextAccessor(IServiceProvider root, TTenant tenant) {
        _root  = root;
        Tenant = tenant;
    }

    #region ITenantContextAccessor<TTenant> Members

    public TTenant? Tenant { get; }

    public Task InitializeAsync(CancellationToken ct) { return Task.CompletedTask; }

    public Task InitializeAsync(TTenant tenant, CancellationToken ct) { return Task.CompletedTask; }

    public Task<IServiceProvider> GetBaseServiceProviderAsync(CancellationToken ct) { return Task.FromResult(_root); }

    #endregion
}
