using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton;
using Xunit;

namespace Schemata.Tenancy.Tests;

public class MemoryCacheTenantProviderCacheShould
{
    [Fact]
    public async Task Lease_Returns_Cached_Instance_On_Second_Call() {
        using var cache = BuildCache();
        using var first = await cache.LeaseVersionAsync("t1", Guid.Empty, BuildProvider);
        using var again = await cache.LeaseVersionAsync("t1", Guid.Empty, () => throw new InvalidOperationException("factory must not run"));

        Assert.Same(first.Provider, again.Provider);
    }

    [Fact]
    public async Task Remove_Defers_Disposal_Until_Last_Lease_Released() {
        using var cache      = BuildCache();
        var       mock       = new Mock<IServiceProvider>();
        var       disposable = mock.As<IDisposable>();
        var       disposed   = 0;
        disposable.Setup(d => d.Dispose()).Callback(() => disposed++);

        var first  = await cache.LeaseVersionAsync("t1", Guid.Empty, () => mock.Object);
        var second = await cache.LeaseVersionAsync("t1", Guid.Empty, () => throw new InvalidOperationException("factory must not run"));

        await cache.RemoveAsync("t1");

        Assert.Equal(0, disposed);

        first.Dispose();
        Assert.Equal(0, disposed);

        second.Dispose();
        Assert.Equal(1, disposed);
    }

    [Fact]
    public async Task Remove_Disposes_Immediately_When_No_Active_Leases() {
        using var cache      = BuildCache();
        var       mock       = new Mock<IServiceProvider>();
        var       disposable = mock.As<IDisposable>();

        (await cache.LeaseVersionAsync("t1", Guid.Empty, () => mock.Object)).Dispose();
        await cache.RemoveAsync("t1");

        disposable.Verify(d => d.Dispose(), Times.Once);
    }

    [Fact]
    public async Task Capacity_Eviction_Defers_Disposal_While_Lease_Active() {
        using var cache = BuildCache(1);

        var firstMock       = new Mock<IServiceProvider>();
        var firstDisposable = firstMock.As<IDisposable>();
        var disposed        = 0;
        firstDisposable.Setup(d => d.Dispose()).Callback(() => disposed++);

        var firstLease  = await cache.LeaseVersionAsync("t1", Guid.Empty, () => firstMock.Object);
        var secondLease = await cache.LeaseVersionAsync("t2", Guid.Empty, () => new Mock<IServiceProvider>().Object);

        Assert.Equal(0, disposed);

        firstLease.Dispose();
        Assert.Equal(1, disposed);

        secondLease.Dispose();
    }

    [Fact]
    public async Task Eviction_Retires_Pinned_Entry_And_Defers_Disposal() {
        using var cache = BuildCache(1);

        var firstMock       = new Mock<IServiceProvider>();
        var firstDisposable = firstMock.As<IDisposable>();
        var disposed        = 0;
        firstDisposable.Setup(d => d.Dispose()).Callback(() => disposed++);

        var firstLease = await cache.LeaseVersionAsync("t1", Guid.Empty, () => firstMock.Object);

        // Adding t2 with capacity=1 retires t1 even though pinned; disposal is deferred.
        var secondLease = await cache.LeaseVersionAsync("t2", Guid.Empty, () => new Mock<IServiceProvider>().Object);

        Assert.Same(firstMock.Object, firstLease.Provider);
        Assert.Equal(0, disposed);

        var rebuiltMock = new Mock<IServiceProvider>();
        var rebuilt     = await cache.LeaseVersionAsync("t1", Guid.Empty, () => rebuiltMock.Object);
        Assert.Same(rebuiltMock.Object, rebuilt.Provider);
        Assert.NotSame(firstLease.Provider, rebuilt.Provider);

        // Releasing the retired lease disposes its provider while the rebuilt one stays alive.
        firstLease.Dispose();
        Assert.Equal(1, disposed);

        rebuilt.Dispose();
        secondLease.Dispose();
    }

    [Fact]
    public async Task Lease_Evicts_Entry_Past_Sliding_Expiration() {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2020-01-01T00:00:00Z"));
        var options = Options.Create(new SchemataTenancyOptions {
            ProviderSlidingExpiration = TimeSpan.FromMinutes(30), ProviderMaxCapacity = 1000,
        });
        using var cache = new MemoryCacheTenantProviderCache(options, clock);

        var mock       = new Mock<IServiceProvider>();
        var disposable = mock.As<IDisposable>();
        var disposed   = 0;
        disposable.Setup(d => d.Dispose()).Callback(() => disposed++);

        (await cache.LeaseVersionAsync("t1", Guid.Empty, () => mock.Object)).Dispose();

        // Advancing past the sliding window makes the next lease sweep the stale entry.
        clock.Advance(TimeSpan.FromMinutes(31));
        (await cache.LeaseVersionAsync("t2", Guid.Empty, () => new Mock<IServiceProvider>().Object)).Dispose();

        Assert.Equal(1, disposed);

        var       rebuilt = new Mock<IServiceProvider>();
        using var lease   = await cache.LeaseVersionAsync("t1", Guid.Empty, () => rebuilt.Object);
        Assert.Same(rebuilt.Object, lease.Provider);
    }

    [Fact]
    public async Task Remove_Disposes_Retired_Provider_Asynchronously_When_Only_Async_Disposable() {
        using var cache = BuildCache();
        var       mock  = new Mock<IServiceProvider>();
        var       async = mock.As<IAsyncDisposable>();
        async.Setup(d => d.DisposeAsync()).Returns(ValueTask.CompletedTask);

        (await cache.LeaseVersionAsync("t1", Guid.Empty, () => mock.Object)).Dispose();
        await cache.RemoveAsync("t1");

        async.Verify(d => d.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task Shutdown_Disposes_Async_Only_Provider_Through_Async_Disposal() {
        using var cache = BuildCache();
        var mock = new Mock<IServiceProvider>();
        var asynchronous = mock.As<IAsyncDisposable>();
        asynchronous.Setup(d => d.DisposeAsync()).Returns(ValueTask.CompletedTask);

        (await cache.LeaseVersionAsync("t1", Guid.Empty, () => mock.Object)).Dispose();

        cache.Dispose();

        asynchronous.Verify(d => d.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task Lease_Dispose_Is_Idempotent() {
        using var cache      = BuildCache();
        var       mock       = new Mock<IServiceProvider>();
        var       disposable = mock.As<IDisposable>();

        var lease = await cache.LeaseVersionAsync("t1", Guid.Empty, () => mock.Object);
        await cache.RemoveAsync("t1");

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

}
