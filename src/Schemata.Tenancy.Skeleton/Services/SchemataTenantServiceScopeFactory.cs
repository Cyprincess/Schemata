using System;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Skeleton.Services;

/// <summary>
///     Creates service scopes using the tenant-isolated service provider when a tenant is resolved,
///     or the root provider otherwise.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public class SchemataTenantServiceScopeFactory<TTenant> : ITenantServiceScopeFactory<TTenant>
    where TTenant : SchemataTenant
{
    private readonly ITenantContextAccessor<TTenant>        _accessor;
    private readonly ITenantServiceProviderFactory<TTenant> _factory;
    private readonly IServiceProvider                       _root;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SchemataTenantServiceScopeFactory{TTenant}" /> class.
    /// </summary>
    public SchemataTenantServiceScopeFactory(
        IServiceProvider                       root,
        ITenantContextAccessor<TTenant>        accessor,
        ITenantServiceProviderFactory<TTenant> factory
    ) {
        _root     = root;
        _accessor = accessor;
        _factory  = factory;
    }

    #region ITenantServiceScopeFactory<TTenant> Members

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
