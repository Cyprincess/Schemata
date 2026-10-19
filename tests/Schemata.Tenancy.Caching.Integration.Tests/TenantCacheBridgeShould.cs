using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Caching.Memory;
using Schemata.Caching.Skeleton;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Tenancy;
using Xunit;

namespace Schemata.Tenancy.Caching.Integration.Tests;

[Trait("Layer", "Integration")]
public class TenantCacheBridgeShould
{
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Outlets_Keep_Their_Bound_Namespace_Under_Ambient_Tenant_Switches() {
        await using var store = TenancyCacheStore.Create(
            services => services.AddMemoryCacheProvider(),
            builder => builder.UseCache(cache => cache.UseShared(new MemoryCacheBackend())));
        var global = store.Root.GetRequiredService<ICacheProvider>();
        await using var scopeA = await store.EnterAsync(TenantA);
        var tenantA = scopeA.ServiceProvider.GetRequiredService<ICacheProvider>();
        await using var scopeB = await store.EnterAsync(TenantB);
        var tenantB = scopeB.ServiceProvider.GetRequiredService<ICacheProvider>();

        await global.SetAsync("key", new byte[] { 0 }, new());
        await tenantA.SetAsync("key", new byte[] { 1 }, new());
        await tenantB.SetAsync("key", new byte[] { 2 }, new());

        using (TenantContext.Enter(new(TenantB))) {
            Assert.Equal(new byte[] { 0 }, await global.GetAsync("key"));
            Assert.Equal(new byte[] { 1 }, await tenantA.GetAsync("key"));
            Assert.Equal(new byte[] { 2 }, await tenantB.GetAsync("key"));
        }
    }

    [Fact]
    public async Task Tenant_Without_UseCache_Resolves_The_Unframed_Host_Provider() {
        await using var store = TenancyCacheStore.Create(services => services
            .AddMemoryCacheProvider()
            .Configure<SchemataTenancyOptions>(_ => { }));
        var host = store.Root.GetRequiredService<ICacheProvider>();
        await using var scope = await store.EnterAsync(TenantA);
        var tenant = scope.ServiceProvider.GetRequiredService<ICacheProvider>();

        Assert.Same(host, tenant);
        await tenant.SetAsync("key", new byte[] { 1 }, new());
        var backend = store.Root.GetRequiredService<MemoryCacheBackend>();
        Assert.Equal(new byte[] { 1 }, await backend.GetAsync("key"));
    }

    [Fact]
    public async Task Repeated_Identical_Shared_Declaration_Composes_One_Outlet_With_One_Frame() {
        var shared = new MemoryCacheBackend();
        await using var store = TenancyCacheStore.Create(tenancy: builder => {
            builder.UseCache(cache => cache.UseShared(shared));
            builder.UseCache(cache => cache.UseShared(shared));
        });
        await using var scope = await store.EnterAsync(TenantA);
        var cache = scope.ServiceProvider.GetRequiredService<ICacheProvider>();
        await cache.SetAsync("key", new byte[] { 9 }, new());

        var frame = TenantA.ToString("N") + '\x1e';
        Assert.Equal(new byte[] { 9 }, await shared.GetAsync(frame + "key"));
        Assert.Null(await shared.GetAsync(frame + frame + "key"));
    }

    [Fact]
    public async Task Conflicting_Declarations_For_One_Outlet_Are_Rejected() {
        var shared = new MemoryCacheBackend();
        await using var mixedModes = TenancyCacheStore.Create(tenancy: builder => {
            builder.UseCache(cache => cache.UseShared(shared));
            builder.UseCache(cache => cache.UseDedicated(_ => new MemoryCacheBackend()));
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => mixedModes.EnterAsync(TenantA).AsTask());

        await using var differingShared = TenancyCacheStore.Create(tenancy: builder => {
            builder.UseCache(cache => cache.UseShared(new MemoryCacheBackend()));
            builder.UseCache(cache => cache.UseShared(new MemoryCacheBackend()));
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => differingShared.EnterAsync(TenantA).AsTask());
    }

    [Fact]
    public async Task Conditional_Writes_Elect_One_Winner_Per_Outlet_And_Collections_Stay_Independent() {
        await using var store = TenancyCacheStore.Create(
            services => services.AddMemoryCacheProvider(),
            builder => builder.UseCache(cache => cache.UseShared(new MemoryCacheBackend())));
        var global = store.Root.GetRequiredService<ICacheProvider>();
        await using var scopeA = await store.EnterAsync(TenantA);
        var tenantA = scopeA.ServiceProvider.GetRequiredService<ICacheProvider>();
        await using var scopeB = await store.EnterAsync(TenantB);
        var tenantB = scopeB.ServiceProvider.GetRequiredService<ICacheProvider>();

        var results = await Task.WhenAll(Enumerable.Range(0, 30).Select(i => Task.Run(async () => {
            var cache = (i % 3) switch { 0 => global, 1 => tenantA, _ => tenantB };
            await Task.Yield();
            return (Outlet: i % 3, Won: await cache.TryAddAsync("same", new byte[] { (byte)i }, new()));
        })));
        foreach (var outlet in new[] { 0, 1, 2 }) {
            Assert.Single(results, result => result.Outlet == outlet && result.Won);
        }

        await tenantA.CollectionAddAsync("set", "one", new());
        await tenantA.CollectionAddAsync("set", "two", new());
        await tenantB.CollectionAddAsync("set", "other", new());
        await tenantA.CollectionRemoveAsync("set", new[] { "one" });
        Assert.Equal("two", Assert.Single((await tenantA.CollectionMembersAsync("set"))!));
        Assert.Equal("other", Assert.Single((await tenantB.CollectionMembersAsync("set"))!));
        await tenantA.CollectionClearAsync("set");
        Assert.Null(await tenantA.CollectionMembersAsync("set"));
        Assert.Equal("other", Assert.Single((await tenantB.CollectionMembersAsync("set"))!));
    }

    [Fact]
    public async Task Dedicated_Outlets_Keep_Identical_Keys_In_Their_Own_Backend() {
        await using var store = TenancyCacheStore.Create(tenancy: builder
            => builder.UseCache(cache => cache.UseDedicated(_ => new MemoryCacheBackend())));
        await using var scopeA = await store.EnterAsync(TenantA);
        await using var scopeB = await store.EnterAsync(TenantB);
        var tenantA = scopeA.ServiceProvider.GetRequiredService<ICacheProvider>();
        var tenantB = scopeB.ServiceProvider.GetRequiredService<ICacheProvider>();

        await tenantA.SetAsync("key", new byte[] { 1 }, new());
        await tenantB.SetAsync("key", new byte[] { 2 }, new());
        Assert.Equal(new byte[] { 1 }, await tenantA.GetAsync("key"));
        Assert.Equal(new byte[] { 2 }, await tenantB.GetAsync("key"));
    }

    [Fact]
    public async Task Configuration_Update_Retires_The_Owned_Backend_After_Outstanding_Leases_Release() {
        var owned = new List<TrackedBackend>();
        await using var store = TenancyCacheStore.Create(tenancy: builder => builder.UseCache(cache
            => cache.UseDedicated(_ => {
                var backend = new TrackedBackend();
                owned.Add(backend);
                return backend;
            })));
        await using var first = await store.EnterAsync(TenantA);
        var cache = first.ServiceProvider.GetRequiredService<ICacheProvider>();
        await cache.SetAsync("key", new byte[] { 1 }, new());
        Assert.Equal(new byte[] { 1 }, await cache.GetAsync("key"));

        store.BumpVersion(TenantA);
        await using var second = await store.EnterAsync(TenantA);
        var fresh = second.ServiceProvider.GetRequiredService<ICacheProvider>();
        Assert.NotSame(cache, fresh);
        Assert.Null(await fresh.GetAsync("key"));
        await fresh.SetAsync("key", new byte[] { 2 }, new());
        Assert.Equal(new byte[] { 2 }, await fresh.GetAsync("key"));

        Assert.False(owned[0].Disposed);
        await first.DisposeAsync();
        Assert.True(owned[0].Disposed);
        Assert.False(owned[1].Disposed);
        Assert.Equal(new byte[] { 2 }, await fresh.GetAsync("key"));
    }

    /// <summary>Decorates one real memory backend with a disposal flag the container can set.</summary>
    private sealed class TrackedBackend : ICacheBackend, IDisposable
    {
        private readonly MemoryCacheBackend _inner = new();

        public bool Disposed { get; private set; }

        public void Dispose() {
            Disposed = true;
            _inner.Dispose();
        }

        public Task<byte[]?> GetAsync(string key, CancellationToken ct = default) { return _inner.GetAsync(key, ct); }

        public Task SetAsync(string key, byte[] value, CacheEntryOptions options, CancellationToken ct = default) {
            return _inner.SetAsync(key, value, options, ct);
        }

        public Task RemoveAsync(string key, CancellationToken ct = default) { return _inner.RemoveAsync(key, ct); }

        public Task<bool> TryAddAsync(string key, byte[] value, CacheEntryOptions options, CancellationToken ct = default) {
            return _inner.TryAddAsync(key, value, options, ct);
        }

        public Task<bool> TryReplaceAsync(string key, byte[] expected, byte[] replacement, CacheEntryOptions options, CancellationToken ct = default) {
            return _inner.TryReplaceAsync(key, expected, replacement, options, ct);
        }

        public Task<bool> TryRemoveAsync(string key, byte[] expected, CancellationToken ct = default) {
            return _inner.TryRemoveAsync(key, expected, ct);
        }

        public Task RefreshAsync(string key, CancellationToken ct = default) { return _inner.RefreshAsync(key, ct); }

        public Task CollectionAddAsync(string key, string member, CacheEntryOptions options, CancellationToken ct = default) {
            return _inner.CollectionAddAsync(key, member, options, ct);
        }

        public Task<IReadOnlyList<string>?> CollectionMembersAsync(string key, CancellationToken ct = default) {
            return _inner.CollectionMembersAsync(key, ct);
        }

        public Task CollectionRemoveAsync(string key, ICollection<string> members, CancellationToken ct = default) {
            return _inner.CollectionRemoveAsync(key, members, ct);
        }

        public Task CollectionRemoveAsync(string key, string member, CancellationToken ct = default) {
            return _inner.CollectionRemoveAsync(key, member, ct);
        }

        public Task CollectionClearAsync(string key, CancellationToken ct = default) { return _inner.CollectionClearAsync(key, ct); }
    }
}
