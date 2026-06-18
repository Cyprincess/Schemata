using System;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Services;
using Xunit;

namespace Schemata.Tenancy.Tests;

public class MemoryCacheTenantProviderCacheShould
{
    [Fact]
    public void Lease_Returns_Cached_Instance_On_Second_Call() {
        using var cache = BuildCache();
        using var first = cache.Lease("t1", BuildProvider);
        using var again = cache.Lease("t1", () => throw new InvalidOperationException("factory must not run"));

        Assert.Same(first.Provider, again.Provider);
    }

    [Fact]
    public void Remove_Defers_Disposal_Until_Last_Lease_Released() {
        using var cache      = BuildCache();
        var       mock       = new Mock<IServiceProvider>();
        var       disposable = mock.As<IDisposable>();
        var       disposed   = 0;
        disposable.Setup(d => d.Dispose()).Callback(() => disposed++);

        var first  = cache.Lease("t1", () => mock.Object);
        var second = cache.Lease("t1", () => throw new InvalidOperationException("factory must not run"));

        cache.Remove("t1");

        Assert.Equal(0, disposed);

        first.Dispose();
        Assert.Equal(0, disposed);

        second.Dispose();
        Assert.Equal(1, disposed);
    }

    [Fact]
    public void Remove_Disposes_Immediately_When_No_Active_Leases() {
        using var cache      = BuildCache();
        var       mock       = new Mock<IServiceProvider>();
        var       disposable = mock.As<IDisposable>();

        cache.Lease("t1", () => mock.Object).Dispose();
        cache.Remove("t1");

        disposable.Verify(d => d.Dispose(), Times.Once);
    }

    [Fact]
    public void Capacity_Eviction_Defers_Disposal_While_Lease_Active() {
        using var cache = BuildCache(1);

        var firstMock       = new Mock<IServiceProvider>();
        var firstDisposable = firstMock.As<IDisposable>();
        var disposed        = 0;
        firstDisposable.Setup(d => d.Dispose()).Callback(() => disposed++);

        var firstLease  = cache.Lease("t1", () => firstMock.Object);
        var secondLease = cache.Lease("t2", () => new Mock<IServiceProvider>().Object);

        Assert.Equal(0, disposed);

        firstLease.Dispose();
        Assert.Equal(1, disposed);

        secondLease.Dispose();
    }

    [Fact]
    public void Eviction_Retires_Pinned_Entry_And_Defers_Disposal() {
        using var cache = BuildCache(1);

        var firstMock       = new Mock<IServiceProvider>();
        var firstDisposable = firstMock.As<IDisposable>();
        var disposed        = 0;
        firstDisposable.Setup(d => d.Dispose()).Callback(() => disposed++);

        // t1 is held by an active lease.
        var firstLease = cache.Lease("t1", () => firstMock.Object);

        // Adding t2 with capacity=1 retires t1 even though pinned; disposal is deferred.
        var secondLease = cache.Lease("t2", () => new Mock<IServiceProvider>().Object);

        // The outstanding lease still points to the original provider.
        Assert.Same(firstMock.Object, firstLease.Provider);
        Assert.Equal(0, disposed);

        // After retirement, a fresh request for the same tenant id rebuilds a new provider.
        var rebuiltMock = new Mock<IServiceProvider>();
        var rebuilt     = cache.Lease("t1", () => rebuiltMock.Object);
        Assert.Same(rebuiltMock.Object, rebuilt.Provider);
        Assert.NotSame(firstLease.Provider, rebuilt.Provider);

        // Releasing the retired lease disposes its provider; the rebuilt one stays alive.
        firstLease.Dispose();
        Assert.Equal(1, disposed);

        rebuilt.Dispose();
        secondLease.Dispose();
    }

    [Fact]
    public void Lease_Evicts_Entry_Past_Sliding_Expiration() {
        var clock = new MutableClock(DateTimeOffset.Parse("2020-01-01T00:00:00Z"));
        var options = Options.Create(new SchemataTenancyOptions {
            ProviderSlidingExpiration = TimeSpan.FromMinutes(30), ProviderMaxCapacity = 1000,
        });
        using var cache = new MemoryCacheTenantProviderCache(options, clock);

        var mock       = new Mock<IServiceProvider>();
        var disposable = mock.As<IDisposable>();
        var disposed   = 0;
        disposable.Setup(d => d.Dispose()).Callback(() => disposed++);

        cache.Lease("t1", () => mock.Object).Dispose();

        // Advancing past the sliding window makes the next lease sweep the stale entry; its provider
        // is disposed because no lease still holds it.
        clock.Advance(TimeSpan.FromMinutes(31));
        cache.Lease("t2", () => new Mock<IServiceProvider>().Object).Dispose();

        Assert.Equal(1, disposed);

        // The swept tenant rebuilds from scratch instead of returning the evicted provider.
        var rebuilt = new Mock<IServiceProvider>();
        using var lease = cache.Lease("t1", () => rebuilt.Object);
        Assert.Same(rebuilt.Object, lease.Provider);
    }

    [Fact]
    public void Lease_Dispose_Is_Idempotent() {
        using var cache      = BuildCache();
        var       mock       = new Mock<IServiceProvider>();
        var       disposable = mock.As<IDisposable>();

        var lease = cache.Lease("t1", () => mock.Object);
        cache.Remove("t1");

        lease.Dispose();
        lease.Dispose();

        disposable.Verify(d => d.Dispose(), Times.Once);
    }

    private static IServiceProvider BuildProvider() { return new Mock<IServiceProvider>().Object; }

    private static MemoryCacheTenantProviderCache BuildCache(int capacity = 1000) {
        var options = Options.Create(new SchemataTenancyOptions {
            ProviderSlidingExpiration = TimeSpan.FromMinutes(30), ProviderMaxCapacity = capacity,
        });
        return new(options);
    }

    private sealed class MutableClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() {
            return _now;
        }

        public void Advance(TimeSpan delta) {
            _now += delta;
        }
    }
}
