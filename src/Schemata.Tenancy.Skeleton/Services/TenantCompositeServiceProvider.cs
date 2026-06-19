using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Tenancy.Skeleton.Services;

/// <summary>
///     <see cref="IServiceProvider" /> that resolves services from a tenant-specific singleton
///     container first, falling back to the host root provider for everything else.
///     <see cref="IServiceScopeFactory" /> is intercepted so scopes created from this provider
///     compose the tenant overrides on top of a fresh host scope. Scoped and Transient
///     services come from the host's request-scope lifecycle while tenant singletons
///     stay pinned to the cached per-tenant container.
/// </summary>
internal sealed class TenantCompositeServiceProvider : IServiceProvider, IDisposable
{
    private readonly ServiceProvider  _overrides;
    private readonly IServiceProvider _root;
    private          bool             _disposed;

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
            return ComposeEnumerable(serviceType, _root, _overrides);
        }

        return _overrides.GetService(serviceType) ?? _root.GetService(serviceType);
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
}
