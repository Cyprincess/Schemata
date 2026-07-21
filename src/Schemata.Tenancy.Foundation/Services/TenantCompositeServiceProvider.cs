using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Tenancy.Foundation.Services;

/// <summary>
///     <see cref="IServiceProvider" /> that resolves services from a tenant-specific singleton
///     container first, falling back to the host root provider for everything else.
///     <see cref="IServiceScopeFactory" /> is intercepted so scopes created from this provider
///     compose the tenant overrides on top of a fresh host scope. Scoped and Transient
///     services come from the host's request-scope lifecycle while tenant singletons
///     stay pinned to the cached per-tenant container.
/// </summary>
internal sealed class TenantCompositeServiceProvider :
    IServiceProvider,
    IServiceScopeFactory,
    IKeyedServiceProvider,
    IServiceProviderIsService,
    IServiceProviderIsKeyedService,
    IDisposable,
    IAsyncDisposable
{
    private readonly ServiceProvider  _overrides;
    private readonly IServiceProvider _root;
    private          int              _disposed;

    /// <summary>Initializes a provider that resolves tenant overrides before host services.</summary>
    public TenantCompositeServiceProvider(ServiceProvider overrides, IServiceProvider root) {
        _overrides = overrides;
        _root      = root;
    }

    /// <summary>Gets the tenant override provider.</summary>
    internal ServiceProvider  Overrides => _overrides;

    /// <summary>Gets the host root provider.</summary>
    internal IServiceProvider Root      => _root;

    #region IDisposable Members

    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _overrides.Dispose();
    }

    public ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) {
            return ValueTask.CompletedTask;
        }

        return _overrides.DisposeAsync();
    }

    #endregion

    #region IServiceProvider Members

    public object? GetService(Type serviceType) {
        if (IsCompositeService(serviceType)) {
            return this;
        }

        if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>)) {
            return ComposeEnumerable(serviceType, _root, _overrides);
        }

        return _overrides.GetService(serviceType) ?? _root.GetService(serviceType);
    }

    #endregion

    #region IServiceScopeFactory Members

    public IServiceScope CreateScope() {
        var hostFactory = _root.GetRequiredService<IServiceScopeFactory>();
        return new CompositeScope(this, hostFactory.CreateScope());
    }

    #endregion

    #region IKeyedServiceProvider Members

    public object? GetKeyedService(Type serviceType, object? serviceKey) {
        var overrides = (IKeyedServiceProvider)_overrides;
        if (IsEnumerableService(serviceType, out _)) {
            var items = overrides.GetKeyedService(serviceType, serviceKey);
            if (items is ICollection { Count: > 0 }) {
                return items;
            }

            return (_root as IKeyedServiceProvider)?.GetKeyedService(serviceType, serviceKey);
        }

        return overrides.GetKeyedService(serviceType, serviceKey)
            ?? (_root as IKeyedServiceProvider)?.GetKeyedService(serviceType, serviceKey);
    }

    public object GetRequiredKeyedService(Type serviceType, object? serviceKey) {
        return GetKeyedService(serviceType, serviceKey)
            ?? throw new InvalidOperationException($"No service for type '{serviceType}' has been registered.");
    }

    #endregion

    #region IServiceProviderIsService Members

    public bool IsService(Type serviceType) {
        if (IsCompositeService(serviceType)) {
            return true;
        }

        return _overrides.GetRequiredService<IServiceProviderIsService>().IsService(serviceType)
            || GetServiceProbe(_root)?.IsService(serviceType) == true;
    }

    #endregion

    #region IServiceProviderIsKeyedService Members

    public bool IsKeyedService(Type serviceType, object? serviceKey) {
        return _overrides.GetRequiredService<IServiceProviderIsKeyedService>().IsKeyedService(serviceType, serviceKey)
            || GetKeyedServiceProbe(_root)?.IsKeyedService(serviceType, serviceKey) == true;
    }

    #endregion

    /// <summary>
    ///     Concatenates an <see cref="IEnumerable{T}" /> resolved from <paramref name="root" /> with
    ///     the same set from <paramref name="overrides" />, preserving registration order (host
    ///     registrations first, tenant additions after) so a tenant override that adds to a
    ///     collection is visible alongside the host's services.
    /// </summary>
    internal static object ComposeEnumerable(Type enumerableType, IServiceProvider root, IServiceProvider overrides) {
        var elementType = enumerableType.GetGenericArguments()[0];
        var items       = new List<object?>();

        if (root.GetService(enumerableType) is IEnumerable fromRoot) {
            foreach (var item in fromRoot) {
                items.Add(item);
            }
        }

        if (overrides.GetService(enumerableType) is IEnumerable fromOverrides) {
            foreach (var item in fromOverrides) {
                items.Add(item);
            }
        }

        var array = Array.CreateInstance(elementType, items.Count);
        for (var i = 0; i < items.Count; i++) {
            array.SetValue(items[i], i);
        }

        return array;
    }

    internal static bool IsEnumerableService(Type serviceType, out Type elementType) {
        if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>)) {
            elementType = serviceType.GetGenericArguments()[0];
            return true;
        }

        elementType = null!;
        return false;
    }

    internal static IServiceProviderIsService? GetServiceProbe(IServiceProvider provider) {
        return provider as IServiceProviderIsService
            ?? provider.GetService(typeof(IServiceProviderIsService)) as IServiceProviderIsService;
    }

    internal static IServiceProviderIsKeyedService? GetKeyedServiceProbe(IServiceProvider provider) {
        return provider as IServiceProviderIsKeyedService
            ?? provider.GetService(typeof(IServiceProviderIsKeyedService)) as IServiceProviderIsKeyedService;
    }

    private static bool IsCompositeService(Type serviceType) {
        return serviceType == typeof(IServiceProvider)
            || serviceType == typeof(IServiceScopeFactory)
            || serviceType == typeof(IKeyedServiceProvider)
            || serviceType == typeof(IServiceProviderIsService)
            || serviceType == typeof(IServiceProviderIsKeyedService);
    }
}
