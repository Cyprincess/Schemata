using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Tenancy;
using Schemata.Caching.Skeleton;
using Xunit;

namespace Schemata.Tenancy.Caching.Integration.Tests;

[Trait("Layer", "Integration")]
public class TenantCacheBridgeShould
{
    private static readonly TenantIdentity A = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly TenantIdentity B = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));

    [Fact]
    public async Task Singleton_Consumers_Use_Current_Identity_For_Values_And_Conditional_Writes() {
        using var provider = Build();
        var cache = provider.GetRequiredService<ICacheProvider>();
        await cache.SetAsync("same", [0], new());
        using (TenantContext.Enter(A)) {
            Assert.True(await cache.TryAddAsync("same", [1], new()));
            using (TenantContext.Enter(B)) {
                Assert.True(await cache.TryAddAsync("same", [2], new()));
                await Task.Yield();
                Assert.False(await cache.TryRemoveAsync("same", [1]));
                Assert.True(await cache.TryReplaceAsync("same", [2], [3], new()));
            }
            Assert.Equal(new byte[] { 1 }, await cache.GetAsync("same"));
            await cache.RemoveAsync("same");
        }
        Assert.Equal(new byte[] { 0 }, await cache.GetAsync("same"));
        using (TenantContext.Enter(B)) Assert.Equal(new byte[] { 3 }, await cache.GetAsync("same"));
        using (TenantContext.Enter(A)) Assert.Null(await cache.GetAsync("same"));
    }

    [Fact]
    public async Task Parallel_Reservations_Have_One_Winner_Per_Identity() {
        using var provider = Build();
        var cache = provider.GetRequiredService<ICacheProvider>();
        var results = await Task.WhenAll(Enumerable.Range(0, 30).Select(i => Task.Run(async () => {
            var identity = (i % 3) switch { 0 => TenantIdentity.Host, 1 => A, _ => B };
            using var frame = TenantContext.Enter(identity);
            await Task.Yield();
            return (Identity: identity, Won: await cache.TryAddAsync("same", [(byte)i], new()));
        })));
        foreach (var identity in new[] { TenantIdentity.Host, A, B }) {
            Assert.Single(results, result => result.Identity == identity && result.Won);
        }
        Assert.Equal(TenantIdentity.Host, TenantContext.Current);
    }

    [Fact]
    public async Task Collection_Removals_And_Clear_Preserve_Other_Identities() {
        using var provider = Build();
        var cache = provider.GetRequiredService<ICacheProvider>();
        await cache.CollectionAddAsync("same", "host", new());
        using (TenantContext.Enter(A)) {
            await cache.CollectionAddAsync("same", "one", new());
            await cache.CollectionAddAsync("same", "two", new());
            await cache.CollectionRemoveAsync("same", new[] { "one" });
            Assert.Equal("two", Assert.Single((await cache.CollectionMembersAsync("same"))!));
        }
        using (TenantContext.Enter(B)) {
            await cache.CollectionAddAsync("same", "other", new());
            await cache.CollectionRemoveAsync("same", "other");
            Assert.Null(await cache.CollectionMembersAsync("same"));
        }
        using (TenantContext.Enter(A)) await cache.CollectionClearAsync("same");
        Assert.Equal("host", Assert.Single((await cache.CollectionMembersAsync("same"))!));
    }

    [Fact]
    public async Task Wrapper_Before_Or_After_Backend_Exposes_One_Tenant_Framed_Outlet() {
        var services = new ServiceCollection();
        services.AddTenantCache();
        services.AddMemoryCacheProvider();
        services.AddTenantCache();
        services.AddTenantCache();
        using var provider = services.BuildServiceProvider();

        var scalar = provider.GetRequiredService<ICacheProvider>();
        Assert.Same(scalar, Assert.Single(provider.GetServices<ICacheProvider>()));

        using (TenantContext.Enter(A)) {
            await scalar.SetAsync("key", [7], new());
        }
        Assert.Null(await scalar.GetAsync("key"));
        using (TenantContext.Enter(A)) {
            Assert.Equal(new byte[] { 7 }, await scalar.GetAsync("key"));
        }
    }

    [Fact]
    public async Task Repeated_Installation_Does_Not_Double_Frame_Tenant_Keys() {
        using var provider = Build(twice: true);

        var scalar = provider.GetRequiredService<ICacheProvider>();
        await scalar.SetAsync("key", [9], new());

        // Inspect the raw keyed backend: a self-wrapped outlet would store "host\x1ehost\x1ekey".
        var backend = provider.GetRequiredKeyedService<ICacheProvider>(CacheServiceKeys.Backend);
        Assert.Equal(new byte[] { 9 }, await backend.GetAsync("host\x1ekey"));
        Assert.Null(await backend.GetAsync("host\x1ehost\x1ekey"));
    }

    [Fact]
    public void Missing_Backend_Fails_On_First_Resolution() {
        var services = new ServiceCollection();
        services.AddTenantCache();
        using var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ICacheProvider>());
    }

    [Fact]
    public async Task Nested_Exception_And_Cancellation_Restore_Parent_Frame() {
        using var outer = TenantContext.Enter(A);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            await Task.Yield();
            using var inner = TenantContext.Enter(B);
            await Task.Yield();
            Assert.Equal(B, TenantContext.Current);
            throw new InvalidOperationException();
        });
        Assert.Equal(A, TenantContext.Current);
        await Assert.ThrowsAsync<OperationCanceledException>(async () => {
            using var inner = TenantContext.Enter(TenantIdentity.Host);
            await Task.Yield();
            throw new OperationCanceledException();
        });
        Assert.Equal(A, TenantContext.Current);
    }

    private static ServiceProvider Build(bool twice = false) {
        var services = new ServiceCollection();
        services.AddMemoryCacheProvider();
        services.AddTenantCache();
        if (twice) {
            services.AddTenantCache();
        }
        return services.BuildServiceProvider();
    }
}
