using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Caching.Integration.Tests;

/// <summary>
///     Tenancy composition over a plain service collection: a fixture tenant manager plus the
///     provider cache and scope factory, so tests lease real per-tenant providers without HTTP.
/// </summary>
internal sealed class TenancyCacheStore : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, SchemataTenant>        _tenants = new();
    private readonly MemoryCacheTenantProviderCache                    _cache;
    private readonly ServiceProvider                                   _root;
    private readonly SchemataTenantServiceScopeFactory<SchemataTenant> _scopes;

    private TenancyCacheStore(
        Action<IServiceCollection>?                     configure,
        Action<SchemataTenancyBuilder<SchemataTenant>>? tenancy
    ) {
        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(value => value.FindByTenantId(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid identifier, CancellationToken _) => new ValueTask<SchemataTenant?>(
                _tenants.TryGetValue(identifier, out var found) ? found : null));
        var services = new ServiceCollection();
        services.AddSingleton(manager.Object);
        tenancy?.Invoke(new SchemataTenancyBuilder<SchemataTenant>(services));
        configure?.Invoke(services);
        _root = services.BuildServiceProvider();
        try {
            var options = _root.GetRequiredService<IOptions<SchemataTenancyOptions>>();
            _cache  = new MemoryCacheTenantProviderCache(options);
            _scopes = new(_root, new SchemataTenantServiceProviderFactory<SchemataTenant>(_root, _cache, options));
        } catch {
            _root.Dispose();
            throw;
        }
    }

    /// <summary>Gets the host root provider holding the global outlets.</summary>
    public IServiceProvider Root => _root;

    public static TenancyCacheStore Create(
        Action<IServiceCollection>?                     configure = null,
        Action<SchemataTenancyBuilder<SchemataTenant>>? tenancy   = null
    ) {
        return new(configure, tenancy);
    }

    // The fixture catalog resolves every requested identifier as an existing tenant.
    public ValueTask<AsyncServiceScope> EnterAsync(Guid? tenant) {
        if (tenant is { } uid) {
            Register(uid);
        }

        return _scopes.CreateAsync(new(tenant));
    }

    // Configuration update: the next lease composes a new provider under the new version.
    public void BumpVersion(Guid uid) {
        Register(uid).Timestamp = Guid.NewGuid();
    }

    public async ValueTask DisposeAsync() {
        await _cache.DisposeAsync();
        await _root.DisposeAsync();
    }

    private SchemataTenant Register(Guid uid) {
        return _tenants.GetOrAdd(uid, static value => new() { Uid = value, Timestamp = Guid.NewGuid(), Name = value.ToString() });
    }
}
