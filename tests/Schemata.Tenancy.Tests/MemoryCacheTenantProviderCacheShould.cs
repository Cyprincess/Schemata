using System;
using System.Threading;
using Microsoft.Extensions.Options;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Services;
using Xunit;

namespace Schemata.Tenancy.Tests;

public class MemoryCacheTenantProviderCacheShould
{
    [Fact]
    public void GetOrAdd_Returns_Cached_Instance_On_Second_Call() {
        using var cache = BuildCache();
        var       first = cache.GetOrAdd("t1", BuildProvider);
        var       again = cache.GetOrAdd("t1", () => throw new InvalidOperationException("factory must not run"));

        Assert.Same(first, again);
    }

    [Fact]
    public void Remove_Disposes_Cached_IDisposable_Provider() {
        using var cache    = BuildCache();
        var       provider = (DisposableProvider)cache.GetOrAdd("t1", BuildProvider);

        cache.Remove("t1");

        Assert.True(provider.Disposed);
    }

    [Fact]
    public void Capacity_Eviction_Disposes_The_Evicted_Provider() {
        using var cache = BuildCache(1);

        var first = (DisposableProvider)cache.GetOrAdd("t1", BuildProvider);
        cache.GetOrAdd("t2", BuildProvider);

        // MemoryCache compaction runs on a background task; wait deterministically.
        Assert.True(SpinUntil(() => first.Disposed, TimeSpan.FromSeconds(5)));
    }

    private static IServiceProvider BuildProvider() { return new DisposableProvider(); }

    private static MemoryCacheTenantProviderCache BuildCache(int capacity = 1000) {
        var options = Options.Create(
            new SchemataTenancyOptions {
                ProviderSlidingExpiration = TimeSpan.FromMinutes(30), ProviderMaxCapacity = capacity,
            }
        );
        return new(options);
    }

    private static bool SpinUntil(Func<bool> predicate, TimeSpan timeout) {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline) {
            if (predicate()) {
                return true;
            }

            Thread.Sleep(25);
        }

        return predicate();
    }

    #region Nested type: DisposableProvider

    private sealed class DisposableProvider : IServiceProvider, IDisposable
    {
        public bool Disposed { get; private set; }

        #region IDisposable Members

        public void Dispose() { Disposed = true; }

        #endregion

        #region IServiceProvider Members

        public object? GetService(Type serviceType) { return null; }

        #endregion
    }

    #endregion
}
