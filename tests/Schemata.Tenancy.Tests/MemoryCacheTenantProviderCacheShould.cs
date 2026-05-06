using System;
using System.Threading;
using Microsoft.Extensions.Options;
using Moq;
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
        using var cache      = BuildCache();
        var       mock       = new Mock<IServiceProvider>();
        var       disposable = mock.As<IDisposable>();

        cache.GetOrAdd("t1", () => mock.Object);
        cache.Remove("t1");

        disposable.Verify(d => d.Dispose(), Times.Once);
    }

    [Fact]
    public void Capacity_Eviction_Disposes_The_Evicted_Provider() {
        using var cache = BuildCache(1);

        var firstMock       = new Mock<IServiceProvider>();
        var firstDisposable = firstMock.As<IDisposable>();
        var disposed        = false;
        firstDisposable.Setup(d => d.Dispose()).Callback(() => disposed = true);

        cache.GetOrAdd("t1", () => firstMock.Object);
        cache.GetOrAdd("t2", () => new Mock<IServiceProvider>().Object);

        Assert.True(SpinUntil(() => disposed, TimeSpan.FromSeconds(5)));
    }

    private static IServiceProvider BuildProvider() { return new Mock<IServiceProvider>().Object; }

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
}
