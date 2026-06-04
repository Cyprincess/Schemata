using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Tenancy.Skeleton.Services;

/// <summary>
///     <see cref="IServiceProvider" /> that resolves services from a tenant-specific singleton
///     container first, falling back to the host root provider for everything else.
///     <see cref="IServiceScopeFactory" /> is intercepted so scopes created from this provider
///     compose the tenant overrides on top of a fresh host scope, ensuring Scoped and Transient
///     services come from the host's normal request-scope lifecycle while tenant singletons
///     stay pinned to the cached per-tenant container.
/// </summary>
internal sealed class TenantCompositeServiceProvider : IServiceProvider, IDisposable
{
    private readonly ServiceProvider  _overrides;
    private readonly IServiceProvider _root;
    private          bool             _disposed;

    public TenantCompositeServiceProvider(ServiceProvider overrides, IServiceProvider root) {
        _overrides = overrides;
        _root      = root;
    }

    internal ServiceProvider  Overrides => _overrides;
    internal IServiceProvider Root      => _root;

    #region IDisposable Members

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _overrides.Dispose();
    }

    #endregion

    #region IServiceProvider Members

    public object? GetService(Type serviceType) {
        if (serviceType == typeof(IServiceScopeFactory)) {
            return new CompositeScopeFactory(this);
        }

        if (serviceType == typeof(IServiceProvider)) {
            return this;
        }

        if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>)) {
            return _root.GetService(serviceType);
        }

        return _overrides.GetService(serviceType) ?? _root.GetService(serviceType);
    }

    #endregion
}