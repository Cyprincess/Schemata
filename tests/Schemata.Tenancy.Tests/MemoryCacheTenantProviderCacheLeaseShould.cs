using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Xunit;

namespace Schemata.Tenancy.Tests;

public class MemoryCacheTenantProviderCacheLeaseShould
{
    [Fact]
    public async Task Concurrent_Leases_For_One_Key_Build_Once_And_Share_The_Provider() {
        const int callers = 8;
        using var cache   = BuildCache();
        using var barrier = new Barrier(callers + 1);
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var provider = BuildProvider();
        var builds   = 0;

        var leases = Enumerable.Range(0, callers)
            .Select(_ => Task.Run(async () => {
                barrier.SignalAndWait();
                return await cache.LeaseVersionAsync("tenant", Guid.Empty, () => {
                    Interlocked.Increment(ref builds);
                    started.Set();
                    release.Wait();
                    return provider;
                });
            }))
            .ToArray();

        barrier.SignalAndWait();
        try {
            Assert.True(started.Wait(TimeSpan.FromSeconds(30)));
        } finally {
            release.Set();
        }

        var resolved = await Task.WhenAll(leases).WaitAsync(TimeSpan.FromSeconds(30));
        try {
            Assert.Equal(1, builds);
            Assert.All(resolved, lease => Assert.Same(provider, lease.Provider));
        } finally {
            foreach (var lease in resolved) {
                lease.Dispose();
            }
        }
    }

    [Fact]
    public async Task Cancelling_A_Waiter_Does_Not_Block_On_The_Build() {
        using var cache   = BuildCache();
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var provider = DisposableProvider();
        var builds   = 0;

        var builder = Task.Run(async () => await cache.LeaseVersionAsync("tenant", Guid.Empty, () => {
            Interlocked.Increment(ref builds);
            started.Set();
            release.Wait();
            return provider.Object;
        }));

        try {
            Assert.True(started.Wait(TimeSpan.FromSeconds(30)));
            using var cancellation = new CancellationTokenSource();
            var waiter = cache.LeaseVersionAsync("tenant", Guid.Empty, () => throw new InvalidOperationException("factory must not run"), cancellation.Token)
                .AsTask();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
            Assert.False(release.IsSet);
        } finally {
            release.Set();
        }

        using var lease = await builder.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(1, builds);
        Assert.Same(provider.Object, lease.Provider);
        lease.Dispose();
        await cache.RemoveAsync("tenant");
        provider.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
    }

    [Fact]
    public async Task Cancelling_The_Builder_Releases_The_Lease_And_Throws() {
        using var cache = BuildCache();
        using var cancellation = new CancellationTokenSource();
        var provider = DisposableProvider();

        var leaseTask = Task.Run(() => cache.LeaseVersionAsync("tenant", Guid.Empty, () => {
            cancellation.Cancel();
            return provider.Object;
        }, cancellation.Token).AsTask());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => leaseTask.WaitAsync(TimeSpan.FromSeconds(30)));

        provider.As<IDisposable>().Verify(value => value.Dispose(), Times.Never);
        cache.Dispose();
        provider.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
    }

    [Fact]
    public async Task Sync_Lease_Disposal_Disposes_Dual_Interface_Provider_With_Async_Only_Service() {
        const string id = "22222222-2222-2222-2222-222222222222";
        var singleton = new Mock<IAsyncDisposable>();
        singleton.Setup(disposable => disposable.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var overrides = new SchemataTenancyOptions { TenantOverrides = { [id] = [s => s.AddSingleton<IAsyncDisposable>(_ => singleton.Object)] } };

        var tenant = new SchemataTenant { Uid = Guid.Parse(id) };
        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(value => value.FindByTenantId(tenant.Uid, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        using var root = new ServiceCollection().AddSingleton(manager.Object).BuildServiceProvider();
        await using var cache = BuildCache();
        var factory = new SchemataTenantServiceProviderFactory<SchemataTenant>(root, cache, Options.Create(overrides));
        var lease = await factory.CreateServiceProviderAsync(tenant.Uid);
        _ = lease.Provider.GetRequiredService<IAsyncDisposable>();

        await cache.RemoveAsync(tenant.Uid.ToString());
        singleton.Verify(disposable => disposable.DisposeAsync(), Times.Never);

        lease.Dispose();
        lease.Dispose();

        singleton.Verify(disposable => disposable.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task Failed_Factory_Can_Be_Retried() {
        using var cache = BuildCache();
        var provider = BuildProvider();
        var attempts = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.LeaseVersionAsync("tenant", Guid.Empty, () => {
            Interlocked.Increment(ref attempts);
            throw new InvalidOperationException("failure");
        }).AsTask());

        using var lease = await cache.LeaseVersionAsync("tenant", Guid.Empty, () => {
            Interlocked.Increment(ref attempts);
            return provider;
        });

        Assert.Equal(2, attempts);
        Assert.Same(provider, lease.Provider);
    }

    [Fact]
    public async Task Same_Key_Reentry_Fails_Fast() {
        using var cache = BuildCache();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Task.Run(() => cache.LeaseVersionAsync("tenant", Guid.Empty, () => {
                cache.LeaseVersionAsync("tenant", Guid.Empty, BuildProvider).AsTask().GetAwaiter().GetResult();
                return BuildProvider();
            }).AsTask()).WaitAsync(TimeSpan.FromSeconds(30)));

        Assert.Contains("cannot reenter the same key", error.Message);
    }

    [Fact]
    public async Task Ancestor_Chain_Reentry_Fails_Fast() {
        using var cache = BuildCache();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Task.Run(() => cache.LeaseVersionAsync("a", Guid.Empty, () => {
                cache.LeaseVersionAsync("b", Guid.Empty, () => {
                    cache.LeaseVersionAsync("a", Guid.Empty, BuildProvider).AsTask().GetAwaiter().GetResult();
                    return BuildProvider();
                }).AsTask().GetAwaiter().GetResult();
                return BuildProvider();
            }).AsTask()).WaitAsync(TimeSpan.FromSeconds(30)));

        Assert.Contains("cannot reenter the same key", error.Message);
    }

    [Fact]
    public async Task Different_Key_Reentry_Completes_Without_Deadlock() {
        using var cache = BuildCache();

        using var outer = await Task.Run(() => cache.LeaseVersionAsync("outer", Guid.Empty, () => {
            using var inner = cache.LeaseVersionAsync("inner", Guid.Empty, BuildProvider).AsTask().GetAwaiter().GetResult();
            return BuildProvider();
        }).AsTask()).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.NotNull(outer.Provider);
    }

    [Fact]
    public async Task Different_Tenant_Lease_Completes_While_A_Factory_Is_Building() {
        using var cache   = BuildCache();
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var first = Task.Run(async () => await cache.LeaseVersionAsync("first", Guid.Empty, () => {
            started.Set();
            release.Wait();
            return BuildProvider();
        }));

        try {
            Assert.True(started.Wait(TimeSpan.FromSeconds(30)));
            using var second = await cache.LeaseVersionAsync("second", Guid.Empty, BuildProvider);
            Assert.NotNull(second.Provider);
        } finally {
            release.Set();
        }

        (await first.WaitAsync(TimeSpan.FromSeconds(30))).Dispose();
    }

    [Fact]
    public async Task Provider_Lost_To_Remove_During_Construction_Is_Disposed_And_Rebuilt() {
        using var cache   = BuildCache();
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var first = BuildProvider(out var firstProbe);
        var second = BuildProvider();
        var builds = 0;
        var leaseTask = Task.Run(async () => await cache.LeaseVersionAsync("tenant", Guid.Empty, () => {
            if (Interlocked.Increment(ref builds) == 1) {
                started.Set();
                release.Wait();
                return first;
            }

            return second;
        }));

        try {
            Assert.True(started.Wait(TimeSpan.FromSeconds(30)));
            await cache.RemoveAsync("tenant");
        } finally {
            release.Set();
        }

        using var lease = await leaseTask.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(2, builds);
        firstProbe.Verify(disposable => disposable.Dispose(), Times.Once);
        Assert.Same(second, lease.Provider);
    }

    [Fact]
    public async Task Async_Only_Tenant_Singleton_Is_Disposed_After_Eviction_And_Last_Async_Lease() {
        const string id = "11111111-1111-1111-1111-111111111111";
        var singleton = new Mock<IAsyncDisposable>();
        singleton.Setup(disposable => disposable.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var overrides = new SchemataTenancyOptions { TenantOverrides = { [id] = [s => s.AddSingleton<IAsyncDisposable>(_ => singleton.Object)] } };

        var tenant = new SchemataTenant { Uid = Guid.Parse(id) };
        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(value => value.FindByTenantId(tenant.Uid, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        using var root = new ServiceCollection().AddSingleton(manager.Object).BuildServiceProvider();
        await using var cache = BuildCache(1);
        var factory = new SchemataTenantServiceProviderFactory<SchemataTenant>(root, cache, Options.Create(overrides));
        var first = await factory.CreateServiceProviderAsync(tenant.Uid);
        _ = first.Provider.GetRequiredService<IAsyncDisposable>();
        using var replacement = await cache.LeaseVersionAsync("replacement", Guid.Empty, BuildProvider);

        await first.DisposeAsync();

        singleton.Verify(disposable => disposable.DisposeAsync(), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Unit")]
    public async Task Expiration_Cleanup_Failure_Balances_Only_The_Undelivered_Lease(bool cached) {
        var clock = new FakeTimeProvider();
        using var cache = BuildCache(time: clock);
        var firstError = new InvalidOperationException("first expired");
        var secondError = new InvalidOperationException("second expired");
        var first = DisposableProvider(firstError);
        var second = DisposableProvider(secondError);
        var pinned = DisposableProvider();
        var current = DisposableProvider();
        (await cache.LeaseVersionAsync("first", Guid.Empty, () => first.Object)).Dispose();
        (await cache.LeaseVersionAsync("second", Guid.Empty, () => second.Object)).Dispose();
        var pinnedLease = await cache.LeaseVersionAsync("pinned", Guid.Empty, () => pinned.Object);
        ITenantProviderLease? currentLease = null;
        clock.Advance(TimeSpan.FromMinutes(15));
        if (cached) currentLease = await cache.LeaseVersionAsync("current", Guid.Empty, () => current.Object);
        clock.Advance(TimeSpan.FromMinutes(16));

        var error = await Assert.ThrowsAsync<AggregateException>(() => cache.LeaseVersionAsync("current", Guid.Empty, () => current.Object).AsTask());

        Assert.Contains(firstError, error.Flatten().InnerExceptions);
        Assert.Contains(secondError, error.Flatten().InnerExceptions);
        first.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
        second.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
        pinned.As<IDisposable>().Verify(value => value.Dispose(), Times.Never);
        await cache.RemoveAsync("current");
        current.As<IDisposable>().Verify(value => value.Dispose(), cached ? Times.Never() : Times.Once());
        currentLease?.Dispose();
        currentLease?.Dispose();
        current.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
        pinnedLease.Dispose();
        pinnedLease.Dispose();
        pinned.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
    }

    [Fact]
    [Trait("Layer", "Unit")]
    public async Task Capacity_Cleanup_Failure_Balances_New_Entry_Before_Shutdown() {
        using var cache = BuildCache(1);
        var failure = new InvalidOperationException("evicted");
        var retired = DisposableProvider(failure);
        var current = DisposableProvider();
        (await cache.LeaseVersionAsync("retired", Guid.Empty, () => retired.Object)).Dispose();

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => cache.LeaseVersionAsync("current", Guid.Empty, () => current.Object).AsTask()));
        current.As<IDisposable>().Verify(value => value.Dispose(), Times.Never);
        cache.Dispose();
        cache.Dispose();
        retired.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
        current.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
    }

    [Fact]
    [Trait("Layer", "Unit")]
    public async Task Published_Entry_Cleanup_Failure_Preserves_A_Concurrently_Acquired_Lease() {
        using var cache = BuildCache(1);
        var failure = new InvalidOperationException("eviction");
        var retired = new Mock<IServiceProvider>();
        var current = DisposableProvider();
        ITenantProviderLease? delivered = null;
        retired.As<IDisposable>().Setup(value => value.Dispose()).Callback(() => {
            delivered = cache.LeaseVersionAsync("current", Guid.Empty, () => throw new InvalidOperationException("already published"))
                .AsTask().GetAwaiter().GetResult();
            throw failure;
        });
        (await cache.LeaseVersionAsync("retired", Guid.Empty, () => retired.Object)).Dispose();

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => cache.LeaseVersionAsync("current", Guid.Empty, () => current.Object).AsTask()));

        Assert.NotNull(delivered);
        Assert.Same(current.Object, delivered.Provider);
        await cache.RemoveAsync("current");
        current.As<IDisposable>().Verify(value => value.Dispose(), Times.Never);
        delivered.Dispose();
        delivered.Dispose();
        current.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
        retired.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Unit")]
    public async Task Delivery_And_Compensating_Release_Failures_Preserve_Both_Errors(bool expiration) {
        var clock = new FakeTimeProvider();
        using var cache = BuildCache(expiration ? 10 : 1, clock);
        var eviction = new InvalidOperationException("eviction");
        var compensation = new InvalidOperationException("compensation");
        var current = DisposableProvider(compensation);
        var retired = new Mock<IServiceProvider>();
        retired.As<IDisposable>().Setup(value => value.Dispose()).Callback(() => {
            cache.RemoveAsync("current").AsTask().GetAwaiter().GetResult();
            throw eviction;
        });
        (await cache.LeaseVersionAsync("retired", Guid.Empty, () => retired.Object)).Dispose();
        if (expiration) clock.Advance(TimeSpan.FromMinutes(31));

        var error = await Assert.ThrowsAsync<AggregateException>(() => cache.LeaseVersionAsync("current", Guid.Empty, () => current.Object).AsTask());

        Assert.Contains(eviction, error.Flatten().InnerExceptions);
        Assert.Contains(compensation, error.Flatten().InnerExceptions);
        await cache.RemoveAsync("current");
        cache.Dispose();
        current.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
        retired.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
    }

    [Fact]
    [Trait("Layer", "Unit")]
    public async Task Remove_Cleans_All_Versions_And_Preserves_The_Held_Lease_After_Failure() {
        using var cache = BuildCache();
        var failure = new InvalidOperationException("removed");
        var retired = DisposableProvider(failure);
        var other = DisposableProvider();
        var pinned = DisposableProvider(failure);
        var lease = await cache.LeaseVersionAsync("tenant", Guid.NewGuid(), () => pinned.Object);
        (await cache.LeaseVersionAsync("tenant", Guid.NewGuid(), () => other.Object)).Dispose();
        (await cache.LeaseVersionAsync("tenant", Guid.NewGuid(), () => retired.Object)).Dispose();

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => cache.RemoveAsync("tenant").AsTask()));
        other.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
        retired.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
        pinned.As<IDisposable>().Verify(value => value.Dispose(), Times.Never);
        await cache.RemoveAsync("tenant");
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => lease.Dispose()));
        lease.Dispose();
        pinned.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Unit")]
    public async Task Shutdown_Cleans_All_Providers_And_Last_Lease_Once_After_Failures(bool asynchronous) {
        var cache = BuildCache();
        var firstError = new InvalidOperationException("first shutdown");
        var secondError = new InvalidOperationException("second shutdown");
        var first = DisposableProvider(firstError, asynchronous);
        var second = DisposableProvider(secondError, asynchronous);
        var other = DisposableProvider(asynchronous: asynchronous);
        var pinned = DisposableProvider(secondError, asynchronous);
        var lease = await cache.LeaseVersionAsync("pinned", Guid.Empty, () => pinned.Object);
        (await cache.LeaseVersionAsync("other", Guid.Empty, () => other.Object)).Dispose();
        (await cache.LeaseVersionAsync("second", Guid.Empty, () => second.Object)).Dispose();
        (await cache.LeaseVersionAsync("first", Guid.Empty, () => first.Object)).Dispose();

        var error = asynchronous
            ? await Assert.ThrowsAsync<AggregateException>(() => cache.DisposeAsync().AsTask())
            : Assert.Throws<AggregateException>(() => cache.Dispose());
        Assert.Contains(firstError, error.Flatten().InnerExceptions);
        Assert.Contains(secondError, error.Flatten().InnerExceptions);
        VerifyDisposal(first, asynchronous, Times.Once());
        VerifyDisposal(second, asynchronous, Times.Once());
        VerifyDisposal(other, asynchronous, Times.Once());
        VerifyDisposal(pinned, asynchronous, Times.Never());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => cache.LeaseVersionAsync("new", Guid.Empty, BuildProvider).AsTask());
        if (asynchronous) {
            Assert.Same(secondError, await Assert.ThrowsAsync<InvalidOperationException>(() => lease.DisposeAsync().AsTask()));
            await lease.DisposeAsync();
            await cache.DisposeAsync();
        } else {
            Assert.Same(secondError, Assert.Throws<InvalidOperationException>(() => lease.Dispose()));
            lease.Dispose();
            cache.Dispose();
        }
        VerifyDisposal(pinned, asynchronous, Times.Once());
        VerifyDisposal(first, asynchronous, Times.Once());
        VerifyDisposal(second, asynchronous, Times.Once());
        VerifyDisposal(other, asynchronous, Times.Once());
    }

    [Fact]
    [Trait("Layer", "Unit")]
    public async Task Factory_And_Expiration_Failures_Are_Preserved_And_Next_Build_Can_Complete() {
        var clock = new FakeTimeProvider();
        using var cache = BuildCache(time: clock);
        var cleanup = new InvalidOperationException("cleanup");
        var construction = new InvalidOperationException("construction");
        var retired = DisposableProvider(cleanup);
        var current = DisposableProvider();
        (await cache.LeaseVersionAsync("retired", Guid.Empty, () => retired.Object)).Dispose();
        clock.Advance(TimeSpan.FromMinutes(31));

        var error = await Assert.ThrowsAsync<AggregateException>(() => cache.LeaseVersionAsync("current", Guid.Empty, () => throw construction).AsTask());

        Assert.Contains(construction, error.Flatten().InnerExceptions);
        Assert.Contains(cleanup, error.Flatten().InnerExceptions);
        var lease = await cache.LeaseVersionAsync("current", Guid.Empty, () => current.Object);
        Assert.Same(current.Object, lease.Provider);
        await cache.RemoveAsync("current");
        current.As<IDisposable>().Verify(value => value.Dispose(), Times.Never);
        lease.Dispose();
        current.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
        retired.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
    }

    private static Mock<IServiceProvider> DisposableProvider(Exception? failure = null, bool asynchronous = false) {
        var provider = new Mock<IServiceProvider>();
        if (asynchronous) {
            var cleanup = provider.As<IAsyncDisposable>().Setup(value => value.DisposeAsync());
            if (failure is null) cleanup.Returns(ValueTask.CompletedTask);
            else cleanup.Throws(failure);
        } else {
            var cleanup = provider.As<IDisposable>().Setup(value => value.Dispose());
            if (failure is not null) cleanup.Throws(failure);
        }
        return provider;
    }

    private static void VerifyDisposal(Mock<IServiceProvider> provider, bool asynchronous, Times times) {
        if (asynchronous) provider.As<IAsyncDisposable>().Verify(value => value.DisposeAsync(), times);
        else provider.As<IDisposable>().Verify(value => value.Dispose(), times);
    }

    private static MemoryCacheTenantProviderCache BuildCache(int capacity = 10, TimeProvider? time = null) {
        return new(Options.Create(new SchemataTenancyOptions { ProviderMaxCapacity = capacity }), time);
    }

    private static IServiceProvider BuildProvider() { return new ServiceCollection().BuildServiceProvider(); }

    private static IServiceProvider BuildProvider(out Mock<IDisposable> probe) {
        var mock     = new Mock<IDisposable>();
        var services = new ServiceCollection();
        services.AddSingleton<IDisposable>(_ => mock.Object);
        var provider = services.BuildServiceProvider();
        _     = provider.GetRequiredService<IDisposable>();
        probe = mock;
        return provider;
    }
}
