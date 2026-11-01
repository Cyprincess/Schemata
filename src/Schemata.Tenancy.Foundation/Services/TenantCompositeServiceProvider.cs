using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Tenancy.Skeleton;

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

    /// <summary>
    ///     Attached by the provider cache once the entry owning this provider is committed.
    ///     Invoking it leases that exact entry again, so every nested scope pins the provider
    ///     independently and parent disposal cannot revoke child usability; a retired entry is
    ///     never rebuilt under the same key. Null when the provider was composed without a
    ///     provider cache — host-only composition needs no tenancy services.
    /// </summary>
    internal Func<ITenantProviderLease?>? LeaseSource { get; set; }

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
        var host = hostFactory.CreateScope();
        try {
            var lease = LeaseSource?.Invoke();
            try { return new CompositeScope(this, host, lease); }
            catch { lease?.Dispose(); throw; }
        } catch { host.Dispose(); throw; }
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
    ///     collection is visible alongside the host's services. A concrete implementation type
    ///     resolved from both providers is one logical service whose tenant registration owns the
    ///     execution boundary, so host items of that type are replaced by the tenant instance —
    ///     the same override-wins rule that governs single-slot resolution; host-internal
    ///     duplicates of a type absent tenant-side keep plain concatenation. Options families
    ///     (<see cref="IConfigureOptions{TOptions}" />, <see cref="IPostConfigureOptions{TOptions}" />,
    ///     <see cref="IValidateOptions{TOptions}" />) are exempt: every registration composes,
    ///     host setups and validators before tenant appliers.
    /// </summary>
    internal static object ComposeEnumerable(Type enumerableType, IServiceProvider root, IServiceProvider overrides) {
        var elementType = enumerableType.GetGenericArguments()[0];
        var composes    = ComposesPerInstance(elementType);
        var tenantTypes = new HashSet<Type>();
        var tenantItems = new List<object?>();

        if (overrides.GetService(enumerableType) is IEnumerable fromOverrides) {
            foreach (var item in fromOverrides) {
                tenantItems.Add(item);
                if (item is not null) {
                    tenantTypes.Add(item.GetType());
                }
            }
        }

        var items = new List<object?>();
        if (root.GetService(enumerableType) is IEnumerable fromRoot) {
            foreach (var item in fromRoot) {
                if (composes || item is null || !tenantTypes.Contains(item.GetType())) {
                    items.Add(item);
                }
            }
        }

        items.AddRange(tenantItems);

        var array = Array.CreateInstance(elementType, items.Count);
        for (var i = 0; i < items.Count; i++) {
            array.SetValue(items[i], i);
        }

        return array;
    }

    private static bool ComposesPerInstance(Type elementType) {
        if (!elementType.IsGenericType) {
            return false;
        }

        var definition = elementType.GetGenericTypeDefinition();
        return definition == typeof(IConfigureOptions<>)
            || definition == typeof(IPostConfigureOptions<>)
            || definition == typeof(IValidateOptions<>);
    }

    internal static bool IsEnumerableService(Type serviceType, [NotNullWhen(true)] out Type? elementType) {
        if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>)) {
            elementType = serviceType.GetGenericArguments()[0];
            return true;
        }

        elementType = null;
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
