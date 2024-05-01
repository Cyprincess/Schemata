using System;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Skeleton.Services;

public class SchemataTenantServiceScopeFactory<TTenant, TKey>(
    ITenantContextAccessor<TTenant, TKey>        accessor,
    ITenantServiceProviderFactory<TTenant, TKey> factory,
    IServiceProvider                             root) : ITenantServiceScopeFactory<TTenant, TKey>
    where TTenant : SchemataTenant<TKey>
    where TKey : struct, IEquatable<TKey>
{
    #region ITenantServiceScopeFactory<TTenant,TKey> Members

    public IServiceScope CreateScope() {
        return accessor.Tenant switch {
            null when root is IServiceScope scoop => scoop,
            null                                  => root.CreateScope(),
            var _                                 => factory.CreateServiceProvider(accessor).CreateScope(),
        };
    }

    #endregion
}
