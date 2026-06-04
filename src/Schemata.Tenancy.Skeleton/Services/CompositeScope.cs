using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Tenancy.Skeleton.Services;

/// <summary>
///     <see cref="IServiceScope" /> that delegates Scoped/Transient resolution to a host
///     <see cref="IServiceScope" /> while keeping tenant override singletons visible on top.
/// </summary>
internal sealed class CompositeScope : IServiceScope, IServiceProvider
{
    private readonly TenantCompositeServiceProvider _composite;
    private readonly IServiceScope                  _hostScope;
    private          bool                           _disposed;

    public CompositeScope(TenantCompositeServiceProvider composite, IServiceScope hostScope) {
        _composite = composite;
        _hostScope = hostScope;
    }

    #region IServiceProvider Members

    public object? GetService(Type serviceType) {
        if (serviceType == typeof(IServiceScopeFactory)) {
            return new CompositeScopeFactory(_composite);
        }

        if (serviceType == typeof(IServiceProvider) || serviceType == typeof(IServiceScope)) {
            return this;
        }

        if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>)) {
            return _hostScope.ServiceProvider.GetService(serviceType);
        }

        return _composite.Overrides.GetService(serviceType) ?? _hostScope.ServiceProvider.GetService(serviceType);
    }

    #endregion

    #region IServiceScope Members

    public IServiceProvider ServiceProvider => this;

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _hostScope.Dispose();
    }

    #endregion
}