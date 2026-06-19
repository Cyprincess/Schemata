using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Skeleton.Services;

/// <summary>
///     <see cref="ITenantContextAccessor{TTenant}" /> implementation used inside per-tenant
///     service providers. The tenant value is bound at construction after request-pipeline
///     resolution.
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

    public Task<IServiceProvider> GetBaseServiceProviderAsync(CancellationToken ct) { return Task.FromResult(_root); }

    #endregion
}
