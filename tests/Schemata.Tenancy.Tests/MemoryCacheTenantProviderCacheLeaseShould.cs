using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
            .Select(_ => Task.Run(() => {
                barrier.SignalAndWait();
                return cache.Lease("tenant", () => {
                    Interlocked.Increment(ref builds);
                    started.Set();
                    release.Wait();
                    return provider;
                });
            }))
            .ToArray();

        barrier.SignalAndWait();
        try {
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        } finally {
            release.Set();
        }

        var resolved = await Task.WhenAll(leases).WaitAsync(TimeSpan.FromSeconds(5));
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
    public void Failed_Factory_Can_Be_Retried() {
        using var cache = BuildCache();
        var provider = BuildProvider();
        var attempts = 0;

        Assert.Throws<InvalidOperationException>(() => cache.Lease("tenant", () => {
            Interlocked.Increment(ref attempts);
            throw new InvalidOperationException("failure");
        }));

        using var lease = cache.Lease("tenant", () => {
            Interlocked.Increment(ref attempts);
            return provider;
        });

        Assert.Equal(2, attempts);
        Assert.Same(provider, lease.Provider);
    }

    [Fact]
    public void Same_Key_Reentry_Fails_Fast() {
        using var cache = BuildCache();

        var error = Assert.Throws<InvalidOperationException>(() => cache.Lease("tenant", () => {
            cache.Lease("tenant", BuildProvider).Dispose();
            return BuildProvider();
        }));

        Assert.Contains("cannot reenter the same key", error.Message);
    }

    [Fact]
    public void Different_Key_Reentry_Completes_Without_Deadlock() {
        using var cache = BuildCache();

        using var outer = cache.Lease("outer", () => {
            using var inner = cache.Lease("inner", BuildProvider);
            return BuildProvider();
        });

        Assert.NotNull(outer.Provider);
    }

    [Fact]
    public async Task Different_Tenant_Lease_Completes_While_A_Factory_Is_Building() {
        using var cache   = BuildCache();
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var first = Task.Run(() => cache.Lease("first", () => {
            started.Set();
            release.Wait();
            return BuildProvider();
        }));

        try {
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            using var second = await Task.Run(() => cache.Lease("second", BuildProvider)).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(second.Provider);
        } finally {
            release.Set();
        }

        (await first.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    [Fact]
    public async Task Provider_Lost_To_Remove_During_Construction_Is_Disposed_And_Rebuilt() {
        using var cache   = BuildCache();
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var first = BuildProvider(out var firstProbe);
        var second = BuildProvider();
        var builds = 0;
        var leaseTask = Task.Run(() => cache.Lease("tenant", () => {
            if (Interlocked.Increment(ref builds) == 1) {
                started.Set();
                release.Wait();
                return first;
            }

            return second;
        }));

        try {
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            cache.Remove("tenant");
        } finally {
            release.Set();
        }

        using var lease = await leaseTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, builds);
        firstProbe.Verify(disposable => disposable.Dispose(), Times.Once);
        Assert.Same(second, lease.Provider);
    }

    [Fact]
    public async Task Async_Only_Tenant_Singleton_Is_Disposed_After_Eviction_And_Last_Async_Lease() {
        const string id = "11111111-1111-1111-1111-111111111111";
        var singleton = new Mock<IAsyncDisposable>();
        singleton.Setup(disposable => disposable.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var overrides = new SchemataTenancyOptions();
        overrides.TenantOverrides[id] = [s => s.AddSingleton<IAsyncDisposable>(_ => singleton.Object)];

        using var root = new ServiceCollection().BuildServiceProvider();
        await using var cache = BuildCache(1);
        var tenant = new SchemataTenant { Uid = Guid.Parse(id) };
        var accessor = new TenantBoundContextAccessor<SchemataTenant>(root, tenant);
        var factory = new SchemataTenantServiceProviderFactory<SchemataTenant>(root, cache, Options.Create(overrides));
        var first = factory.CreateServiceProvider(accessor);
        _ = first.Provider.GetRequiredService<IAsyncDisposable>();
        using var replacement = cache.Lease("replacement", BuildProvider);

        await ((IAsyncDisposable)first).DisposeAsync();

        singleton.Verify(disposable => disposable.DisposeAsync(), Times.Once);
    }

    private static MemoryCacheTenantProviderCache BuildCache(int capacity = 10) {
        return new(Options.Create(new SchemataTenancyOptions { ProviderMaxCapacity = capacity }));
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
