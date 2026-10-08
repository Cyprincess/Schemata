using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Caching.Memory;
using Schemata.Caching.Skeleton;
using Xunit;

namespace Schemata.Caching.Memory.Integration.Tests;

[Trait("Layer", "Integration")]
public class CacheProviderRegistrationShould
{
    [Fact]
    public async Task Last_Explicit_Selection_Serves_Operations_In_Either_Order() {
        await AssertSelectedServes(secondRegisteredLast: true);
        await AssertSelectedServes(secondRegisteredLast: false);
    }

    private static async Task AssertSelectedServes(bool secondRegisteredLast) {
        // Factory-created singletons are container-owned: each order needs fresh backends.
        var first    = new MemoryCacheProvider();
        var second   = new MemoryCacheProvider();
        var selected = secondRegisteredLast ? second : first;
        var other    = secondRegisteredLast ? first : second;

        using var provider = Build(services => {
            if (secondRegisteredLast) {
                services.AddCacheProvider(_ => first);
                services.AddCacheProvider(_ => second);
            } else {
                services.AddCacheProvider(_ => second);
                services.AddCacheProvider(_ => first);
            }
        });

        var outlet = provider.GetRequiredService<ICacheProvider>();
        await outlet.SetAsync("key", [1], new());
        Assert.True(await outlet.TryAddAsync("key", [2], new()) is false);
        Assert.Equal(new byte[] { 1 }, await selected.GetAsync("key"));
        Assert.Null(await other.GetAsync("key"));
    }

    [Fact]
    public async Task AddMemoryCacheProvider_After_A_Factory_Selection_Serves_The_Typed_Backend() {
        var factory = new MemoryCacheProvider();
        await factory.SetAsync("factory-only", [9], new());

        using var provider = Build(services => {
            services.AddCacheProvider(_ => factory);
            services.AddMemoryCacheProvider();
        });
        var outlet = provider.GetRequiredService<ICacheProvider>();

        Assert.Null(await outlet.GetAsync("factory-only"));

        await outlet.SetAsync("typed", [1], new());
        Assert.Equal(new byte[] { 1 }, await provider.GetRequiredService<MemoryCacheProvider>().GetAsync("typed"));
        Assert.Null(await factory.GetAsync("typed"));
    }

    [Fact]
    public async Task Repeated_Same_Selection_Shares_State_Through_One_Outlet() {
        using var provider = Build(services => {
            services.AddMemoryCacheProvider();
            services.AddMemoryCacheProvider();
        });

        var scalar     = provider.GetRequiredService<ICacheProvider>();
        var enumerable = Assert.Single(provider.GetServices<ICacheProvider>());

        await scalar.SetAsync("key", [1], new());
        Assert.Equal(new byte[] { 1 }, await enumerable.GetAsync("key"));

        await enumerable.CollectionAddAsync("set", "one", new());
        Assert.Equal("one", Assert.Single((await scalar.CollectionMembersAsync("set"))!));
        Assert.Same(scalar, enumerable);
    }

    [Fact]
    public async Task Conditional_And_Collection_Operations_Share_The_Selected_Backend_State() {
        using var provider = Build(services => services.AddMemoryCacheProvider());
        var scalar     = provider.GetRequiredService<ICacheProvider>();
        var enumerable = Assert.Single(provider.GetServices<ICacheProvider>());

        Assert.True(await scalar.TryAddAsync("key", [1], new()));
        Assert.Equal(new byte[] { 1 }, await scalar.GetAsync("key"));
        Assert.True(await scalar.TryReplaceAsync("key", [1], [2], new()));
        Assert.Equal(new byte[] { 2 }, await enumerable.GetAsync("key"));

        await enumerable.CollectionAddAsync("set", "one", new());
        await enumerable.CollectionAddAsync("set", "two", new());
        await scalar.CollectionRemoveAsync("set", "one");
        Assert.Equal("two", Assert.Single((await scalar.CollectionMembersAsync("set"))!));
        await enumerable.CollectionClearAsync("set");
        Assert.Null(await scalar.CollectionMembersAsync("set"));
    }

    [Fact]
    public void Missing_Backend_Fails_On_First_Resolution() {
        using var provider = new ServiceCollection().BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ICacheProvider>());
    }

    private static ServiceProvider Build(Action<IServiceCollection> configure) {
        var services = new ServiceCollection();
        configure(services);
        return services.BuildServiceProvider();
    }
}
