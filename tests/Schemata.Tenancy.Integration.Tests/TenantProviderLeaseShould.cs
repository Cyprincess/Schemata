using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Schemata.Tenancy.Skeleton.Tenancy;
using Xunit;

namespace Schemata.Tenancy.Integration.Tests;

[Trait("Layer", "Integration")]
public class TenantProviderLeaseShould
{
    [Theory]
    [InlineData("capacity")]
    [InlineData("expiration")]
    [InlineData("remove")]
    [InlineData("shutdown")]
    public async Task Public_Tenant_Scope_Keeps_Real_Provider_Alive_Until_Disposal(string retirement) {
        var tenant = new SchemataTenant { Uid = Guid.NewGuid(), Timestamp = Guid.NewGuid() };
        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(value => value.FindByTenantId(tenant.Uid, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        var singleton = new Mock<IDisposable>();
        var scoped = new Mock<IDisposable>();
        var clock = new FakeTimeProvider();
        var options = new SchemataTenancyOptions { ProviderMaxCapacity = 1 };
        options.DynamicOverrides.Add((_, services, _) => services.AddKeyedSingleton<IDisposable>("tenant", (_, _) => singleton.Object));
        await using var root = new ServiceCollection()
            .AddSingleton(manager.Object)
            .AddSingleton<ITenantProviderCache>(_ => new MemoryCacheTenantProviderCache(Options.Create(options), clock))
            .AddKeyedScoped<IDisposable>("host", (_, _) => scoped.Object)
            .BuildServiceProvider();
        var cache = root.GetRequiredService<ITenantProviderCache>();
        var providers = new SchemataTenantServiceProviderFactory<SchemataTenant>(root, cache, Options.Create(options));
        var factory = new SchemataTenantServiceScopeFactory<SchemataTenant>(root, providers);
        var scope = await factory.CreateAsync(new TenantIdentity(tenant.Uid));
        Assert.Same(tenant, scope.ServiceProvider.GetRequiredService<SchemataTenant>());
        Assert.Same(singleton.Object, scope.ServiceProvider.GetRequiredKeyedService<IDisposable>("tenant"));
        Assert.Same(scoped.Object, scope.ServiceProvider.GetRequiredKeyedService<IDisposable>("host"));

        if (retirement == "remove") await cache.RemoveAsync(tenant.Uid.ToString());
        else if (retirement == "shutdown") await root.DisposeAsync();
        else {
            if (retirement == "expiration") clock.Advance(TimeSpan.FromMinutes(31));
            using var replacement = await cache.LeaseVersionAsync("replacement", Guid.Empty, () => new ServiceCollection().BuildServiceProvider());
        }
        singleton.Verify(value => value.Dispose(), Times.Never);
        scoped.Verify(value => value.Dispose(), Times.Never);

        await scope.DisposeAsync();
        await scope.DisposeAsync();
        singleton.Verify(value => value.Dispose(), Times.Once);
        scoped.Verify(value => value.Dispose(), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Tenant_Scope_Cleanup_Failure_Still_Cleans_Host_And_Releases_Retired_Provider(bool asynchronous) {
        var tenant = new SchemataTenant { Uid = Guid.NewGuid(), Timestamp = Guid.NewGuid() };
        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(value => value.FindByTenantId(tenant.Uid, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        var failure = new InvalidOperationException("tenant scope cleanup");
        var tenantScope = new Mock<IDisposable>();
        tenantScope.Setup(value => value.Dispose()).Throws(failure);
        var hostScope = new Mock<IDisposable>();
        var singleton = new Mock<IDisposable>();
        var options = new SchemataTenancyOptions();
        options.DynamicOverrides.Add((_, services, _) => {
            services.AddKeyedScoped<IDisposable>("tenant-scope", (_, _) => tenantScope.Object);
            services.AddKeyedSingleton<IDisposable>("tenant", (_, _) => singleton.Object);
        });
        await using var root = new ServiceCollection().AddSingleton(manager.Object)
            .AddKeyedScoped<IDisposable>("host", (_, _) => hostScope.Object).BuildServiceProvider();
        using var cache = new MemoryCacheTenantProviderCache(Options.Create(options));
        var providers = new SchemataTenantServiceProviderFactory<SchemataTenant>(root, cache, Options.Create(options));
        var factory = new SchemataTenantServiceScopeFactory<SchemataTenant>(root, providers);
        var scope = await factory.CreateAsync(new TenantIdentity(tenant.Uid));
        _ = scope.ServiceProvider.GetRequiredKeyedService<IDisposable>("tenant-scope");
        _ = scope.ServiceProvider.GetRequiredKeyedService<IDisposable>("tenant");
        _ = scope.ServiceProvider.GetRequiredKeyedService<IDisposable>("host");
        await cache.RemoveAsync(tenant.Uid.ToString());
        singleton.Verify(value => value.Dispose(), Times.Never);

        if (asynchronous) Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => scope.DisposeAsync().AsTask()));
        else Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => scope.Dispose()));
        await scope.DisposeAsync();

        tenantScope.Verify(value => value.Dispose(), Times.Once);
        hostScope.Verify(value => value.Dispose(), Times.Once);
        singleton.Verify(value => value.Dispose(), Times.Once);
    }

    [Fact]
    public async Task Child_Scope_Keeps_Provider_Alive_After_Parent_Disposal_And_Cache_Retirement() {
        var tenant = new SchemataTenant { Uid = Guid.NewGuid(), Timestamp = Guid.NewGuid() };
        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(value => value.FindByTenantId(tenant.Uid, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        var singleton = new Mock<IDisposable>();
        var options = new SchemataTenancyOptions();
        options.DynamicOverrides.Add((_, services, _) => services.AddKeyedSingleton<IDisposable>("tenant", (_, _) => singleton.Object));
        await using var root = new ServiceCollection()
            .AddSingleton(manager.Object)
            .AddSingleton<ITenantProviderCache>(_ => new MemoryCacheTenantProviderCache(Options.Create(options)))
            .BuildServiceProvider();
        var cache = root.GetRequiredService<ITenantProviderCache>();
        var providers = new SchemataTenantServiceProviderFactory<SchemataTenant>(root, cache, Options.Create(options));
        var factory = new SchemataTenantServiceScopeFactory<SchemataTenant>(root, providers);
        var parent = await factory.CreateAsync(new TenantIdentity(tenant.Uid));
        var child = parent.ServiceProvider.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();

        await parent.DisposeAsync();
        await cache.RemoveAsync(tenant.Uid.ToString());
        singleton.Verify(value => value.Dispose(), Times.Never);
        Assert.Same(singleton.Object, child.ServiceProvider.GetRequiredKeyedService<IDisposable>("tenant"));

        await child.DisposeAsync();
        await child.DisposeAsync();
        singleton.Verify(value => value.Dispose(), Times.Once);
    }

    [Fact]
    public async Task Nested_Scope_Keeps_Resolving_The_Retired_Version_After_Tenant_Update() {
        var uid = Guid.NewGuid();
        var first = new SchemataTenant { Uid = uid, Timestamp = Guid.NewGuid() };
        var second = new SchemataTenant { Uid = uid, Timestamp = Guid.NewGuid() };
        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(value => value.FindByTenantId(uid, It.IsAny<CancellationToken>())).ReturnsAsync(first);
        var markers = new Dictionary<Guid, Mock<IDisposable>>();
        Mock<IDisposable> MarkerFor(SchemataTenant owner) {
            lock (markers) {
                if (!markers.TryGetValue(owner.Timestamp, out var marker)) {
                    marker = new Mock<IDisposable>();
                    markers.Add(owner.Timestamp, marker);
                }
                return marker;
            }
        }
        var options = new SchemataTenancyOptions();
        options.DynamicOverrides.Add((_, services, _) => services.AddKeyedSingleton<IDisposable>("marker", (provider, _) => {
            var owner = provider.GetRequiredService<SchemataTenant>();
            return MarkerFor(owner).Object;
        }));
        await using var root = new ServiceCollection()
            .AddSingleton(manager.Object)
            .AddSingleton<ITenantProviderCache>(_ => new MemoryCacheTenantProviderCache(Options.Create(options)))
            .BuildServiceProvider();
        var cache = root.GetRequiredService<ITenantProviderCache>();
        var providers = new SchemataTenantServiceProviderFactory<SchemataTenant>(root, cache, Options.Create(options));
        var factory = new SchemataTenantServiceScopeFactory<SchemataTenant>(root, providers);
        var stale = await factory.CreateAsync(new TenantIdentity(uid));
        var staleChild = stale.ServiceProvider.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        var staleMarker = stale.ServiceProvider.GetRequiredKeyedService<IDisposable>("marker");
        var staleChildMarker = staleChild.ServiceProvider.GetRequiredKeyedService<IDisposable>("marker");

        manager.Setup(value => value.FindByTenantId(uid, It.IsAny<CancellationToken>())).ReturnsAsync(second);
        await cache.RemoveAsync(uid.ToString());
        var fresh = await factory.CreateAsync(new TenantIdentity(uid));
        var freshChild = fresh.ServiceProvider.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        var freshMarker = fresh.ServiceProvider.GetRequiredKeyedService<IDisposable>("marker");
        var freshChildMarker = freshChild.ServiceProvider.GetRequiredKeyedService<IDisposable>("marker");

        Assert.Same(second, fresh.ServiceProvider.GetRequiredService<SchemataTenant>());
        Assert.Same(second, freshChild.ServiceProvider.GetRequiredService<SchemataTenant>());
        Assert.Same(first, staleChild.ServiceProvider.GetRequiredService<SchemataTenant>());
        Assert.Same(markers[first.Timestamp].Object, staleMarker);
        Assert.Same(markers[first.Timestamp].Object, staleChildMarker);
        Assert.Same(markers[second.Timestamp].Object, freshMarker);
        Assert.Same(markers[second.Timestamp].Object, freshChildMarker);
        Assert.NotSame(staleMarker, freshMarker);
        markers[first.Timestamp].Verify(value => value.Dispose(), Times.Never);
        markers[second.Timestamp].Verify(value => value.Dispose(), Times.Never);

        await stale.DisposeAsync();
        await staleChild.DisposeAsync();
        markers[first.Timestamp].Verify(value => value.Dispose(), Times.Once);
        markers[second.Timestamp].Verify(value => value.Dispose(), Times.Never);

        // A live cached entry with no outstanding leases stays resident for reuse; retiring it
        // is what disposes the provider with its version's marker.
        await fresh.DisposeAsync();
        await freshChild.DisposeAsync();
        markers[second.Timestamp].Verify(value => value.Dispose(), Times.Never);

        await cache.RemoveAsync(uid.ToString());
        markers[second.Timestamp].Verify(value => value.Dispose(), Times.Once);
    }

    [Fact]
    public async Task Child_Scope_Survives_Parent_Dispose_Failure_And_Releases_The_Last_Lease() {
        var tenant = new SchemataTenant { Uid = Guid.NewGuid(), Timestamp = Guid.NewGuid() };
        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(value => value.FindByTenantId(tenant.Uid, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        var failure = new InvalidOperationException("tenant scope cleanup");
        var tenantScope = new Mock<IDisposable>();
        tenantScope.Setup(value => value.Dispose()).Throws(failure);
        var singleton = new Mock<IDisposable>();
        var options = new SchemataTenancyOptions();
        options.DynamicOverrides.Add((_, services, _) => {
            services.AddKeyedScoped<IDisposable>("tenant-scope", (_, _) => tenantScope.Object);
            services.AddKeyedSingleton<IDisposable>("tenant", (_, _) => singleton.Object);
        });
        await using var root = new ServiceCollection()
            .AddSingleton(manager.Object)
            .AddSingleton<ITenantProviderCache>(_ => new MemoryCacheTenantProviderCache(Options.Create(options)))
            .BuildServiceProvider();
        var cache = root.GetRequiredService<ITenantProviderCache>();
        var providers = new SchemataTenantServiceProviderFactory<SchemataTenant>(root, cache, Options.Create(options));
        var factory = new SchemataTenantServiceScopeFactory<SchemataTenant>(root, providers);
        var parent = await factory.CreateAsync(new TenantIdentity(tenant.Uid));
        _ = parent.ServiceProvider.GetRequiredKeyedService<IDisposable>("tenant-scope");
        var child = parent.ServiceProvider.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        _ = child.ServiceProvider.GetRequiredKeyedService<IDisposable>("tenant-scope");
        _ = child.ServiceProvider.GetRequiredKeyedService<IDisposable>("tenant");
        await cache.RemoveAsync(tenant.Uid.ToString());

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => parent.DisposeAsync().AsTask()));
        Assert.Same(singleton.Object, child.ServiceProvider.GetRequiredKeyedService<IDisposable>("tenant"));
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => child.DisposeAsync().AsTask()));
        singleton.Verify(value => value.Dispose(), Times.Once);
    }

    [Fact]
    public async Task Host_Identity_Creates_Root_Scope_Without_Tenant_Resolution() {
        var manager = new Mock<ITenantManager<SchemataTenant>>(MockBehavior.Strict);
        await using var root = new ServiceCollection().AddSingleton(manager.Object).BuildServiceProvider();
        using var cache = new MemoryCacheTenantProviderCache(Options.Create(new SchemataTenancyOptions()));
        var providers = new SchemataTenantServiceProviderFactory<SchemataTenant>(root, cache, Options.Create(new SchemataTenancyOptions()));
        var factory = new SchemataTenantServiceScopeFactory<SchemataTenant>(root, providers);

        await using var scope = await factory.CreateAsync(TenantIdentity.Host);

        Assert.Null(scope.ServiceProvider.GetService<SchemataTenant>());
        manager.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Retired_Provider_Keeps_In_Flight_Fixed_Protector_Usable_Until_Scope_Release() {
        var tenant = new SchemataTenant { Uid = Guid.NewGuid(), Timestamp = Guid.NewGuid() };
        var other = new SchemataTenant { Uid = Guid.NewGuid(), Timestamp = Guid.NewGuid() };
        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(value => value.FindByTenantId(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid identifier, CancellationToken _) => new ValueTask<SchemataTenant?>(
                identifier == tenant.Uid ? tenant : identifier == other.Uid ? other : null));
        var clock = new FakeTimeProvider();
        var services = new ServiceCollection();
        services.AddSingleton(manager.Object);
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.Configure<SchemataTenancyOptions>(options => options.ProviderMaxCapacity = 1);
        new SchemataTenancyBuilder<SchemataTenant>(services).UseState(state => state.CookiePrefix = "lease");
        await using var root = services.BuildServiceProvider();
        var options = root.GetRequiredService<IOptions<SchemataTenancyOptions>>();
        using var cache = new MemoryCacheTenantProviderCache(options, clock);
        var providers = new SchemataTenantServiceProviderFactory<SchemataTenant>(root, cache, options);
        var factory = new SchemataTenantServiceScopeFactory<SchemataTenant>(root, providers);

        await using var scope = await factory.CreateAsync(new(tenant.Uid));
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("continuation");
        var token = protector.Protect(new byte[] { 1 });

        // The retired version stays leased by the live scope; the in-flight protector keeps
        // serving the owning tenant only while that lease exists.
        await cache.RemoveAsync(tenant.Uid.ToString());
        await using var successor = await factory.CreateAsync(new(tenant.Uid));
        await using var neighbor = await factory.CreateAsync(new(other.Uid));
        var neighborProtector = neighbor.ServiceProvider.GetRequiredService<IDataProtectionProvider>();

        using (TenantContext.Enter(new(other.Uid))) {
            Assert.Equal(new byte[] { 1 }, protector.Unprotect(token));
            Assert.Throws<CryptographicException>(() => ((IDataProtector)neighborProtector).Unprotect(token));
        }
    }
}
