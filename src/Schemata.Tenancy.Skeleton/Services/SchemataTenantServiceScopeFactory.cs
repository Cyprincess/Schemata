using System;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Skeleton.Services;

/// <summary>
///     Creates service scopes using the tenant-isolated service provider when a tenant is resolved,
///     or the root provider otherwise.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
/// <typeparam name="TKey">The tenant identifier type.</typeparam>
public class SchemataTenantServiceScopeFactory<TTenant, TKey> : ITenantServiceScopeFactory<TTenant, TKey>
    where TTenant : SchemataTenant<TKey>
    where TKey : struct, IEquatable<TKey>
{
    private readonly ITenantContextAccessor<TTenant, TKey>        _accessor;
    private readonly ITenantServiceProviderFactory<TTenant, TKey> _factory;
    private readonly IServiceProvider                             _root;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SchemataTenantServiceScopeFactory{TTenant, TKey}" /> class.
    /// </summary>
    public SchemataTenantServiceScopeFactory(
        IServiceProvider                             root,
        ITenantContextAccessor<TTenant, TKey>        accessor,
        ITenantServiceProviderFactory<TTenant, TKey> factory
    ) {
        _root     = root;
        _accessor = accessor;
        _factory  = factory;
    }

    #region ITenantServiceScopeFactory<TTenant,TKey> Members

    /// <inheritdoc />
    public IServiceScope CreateScope() {
        return _accessor.Tenant switch {
            null when _root is IServiceScope scoop => scoop,
            null                                   => _root.CreateScope(),
            var _                                  => _factory.CreateServiceProvider(_accessor).CreateScope(),
        };
    }

    #endregion
}
