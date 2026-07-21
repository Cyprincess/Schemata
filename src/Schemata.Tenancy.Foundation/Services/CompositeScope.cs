using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Tenancy.Foundation.Services;

/// <summary>
///     <see cref="IServiceScope" /> that delegates Scoped/Transient resolution to a host
///     <see cref="IServiceScope" /> while keeping tenant override singletons visible on top.
/// </summary>
internal sealed class CompositeScope :
    IServiceScope,
    IServiceProvider,
    IServiceScopeFactory,
    IKeyedServiceProvider,
    IServiceProviderIsService,
    IServiceProviderIsKeyedService,
    IAsyncDisposable
{
    private readonly TenantCompositeServiceProvider _composite;
    private readonly IServiceScope                  _hostScope;
    private          int                            _disposed;

    /// <summary>Initializes a composite scope over tenant overrides and a host scope.</summary>
    public CompositeScope(TenantCompositeServiceProvider composite, IServiceScope hostScope) {
        _composite = composite;
        _hostScope = hostScope;
    }

    #region IServiceProvider Members

    public object? GetService(Type serviceType) {
        if (IsCompositeService(serviceType)) {
            return this;
        }

        if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>)) {
            return TenantCompositeServiceProvider.ComposeEnumerable(serviceType, _hostScope.ServiceProvider, _composite.Overrides);
        }

        return _composite.Overrides.GetService(serviceType) ?? _hostScope.ServiceProvider.GetService(serviceType);
    }

    #endregion

    #region IServiceScopeFactory Members

    public IServiceScope CreateScope() { return _composite.CreateScope(); }

    #endregion

    #region IKeyedServiceProvider Members

    public object? GetKeyedService(Type serviceType, object? serviceKey) {
        var overrides = (IKeyedServiceProvider)_composite.Overrides;
        if (TenantCompositeServiceProvider.IsEnumerableService(serviceType, out _)) {
            var items = overrides.GetKeyedService(serviceType, serviceKey);
            if (items is System.Collections.ICollection { Count: > 0 }) {
                return items;
            }

            return (_hostScope.ServiceProvider as IKeyedServiceProvider)?.GetKeyedService(serviceType, serviceKey);
        }

        return overrides.GetKeyedService(serviceType, serviceKey)
            ?? (_hostScope.ServiceProvider as IKeyedServiceProvider)?.GetKeyedService(serviceType, serviceKey);
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

        return _composite.Overrides.GetRequiredService<IServiceProviderIsService>().IsService(serviceType)
            || TenantCompositeServiceProvider.GetServiceProbe(_hostScope.ServiceProvider)?.IsService(serviceType) == true;
    }

    #endregion

    #region IServiceProviderIsKeyedService Members

    public bool IsKeyedService(Type serviceType, object? serviceKey) {
        return _composite.Overrides.GetRequiredService<IServiceProviderIsKeyedService>().IsKeyedService(serviceType, serviceKey)
            || TenantCompositeServiceProvider.GetKeyedServiceProbe(_hostScope.ServiceProvider)?.IsKeyedService(serviceType, serviceKey) == true;
    }

    #endregion

    #region IServiceScope Members

    public IServiceProvider ServiceProvider => this;

    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _hostScope.Dispose();
    }

    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        if (_hostScope is IAsyncDisposable disposable) {
            await disposable.DisposeAsync();
        } else {
            _hostScope.Dispose();
        }
    }

    #endregion

    private static bool IsCompositeService(Type serviceType) {
        return serviceType == typeof(IServiceProvider)
            || serviceType == typeof(IServiceScope)
            || serviceType == typeof(IServiceScopeFactory)
            || serviceType == typeof(IKeyedServiceProvider)
            || serviceType == typeof(IServiceProviderIsService)
            || serviceType == typeof(IServiceProviderIsKeyedService);
    }
}
