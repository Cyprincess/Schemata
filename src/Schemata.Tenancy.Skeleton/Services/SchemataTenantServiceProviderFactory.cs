using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Skeleton.Services;

public class SchemataTenantServiceProviderFactory
{
    protected static readonly ConcurrentDictionary<string, Lazy<IServiceProvider>> Providers = [];
}

public class SchemataTenantServiceProviderFactory<TTenant, TKey>(IServiceCollection services, Action<IServiceCollection, TTenant?> configure)
    : SchemataTenantServiceProviderFactory, ITenantServiceProviderFactory<TTenant, TKey>
    where TTenant : SchemataTenant<TKey>
    where TKey : struct, IEquatable<TKey>
{
    #region ITenantServiceProviderFactory<TTenant,TKey> Members

    public IServiceProvider CreateServiceProvider(ITenantContextAccessor<TTenant, TKey> accessor) {
        var id = accessor.Tenant?.TenantId?.ToString();

        if (string.IsNullOrWhiteSpace(id)) {
            throw new InvalidOperationException("Tenant is not initialized successfully.");
        }

        return Providers.GetOrAdd(id!, _ => new(() => {
            var container = new ServiceCollection();

            foreach (var service in services) {
                if (service.ServiceType == typeof(ITenantContextAccessor<TTenant, TKey>)) {
                    container.TryAddSingleton(accessor);

                    continue;
                }

                if (typeof(ITenantContextAccessor<TTenant, TKey>).IsAssignableFrom(service.ServiceType)) {
                    continue;
                }

                container.Add(service);
            }

            configure(container, accessor.Tenant);

            return container.BuildServiceProvider();
        })).Value;
    }

    #endregion
}
