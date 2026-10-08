using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Schemata.Abstractions.Tenancy;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
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

        if (retirement == "remove") cache.Remove(tenant.Uid.ToString());
        else if (retirement == "shutdown") await root.DisposeAsync();
        else {
            if (retirement == "expiration") clock.Advance(TimeSpan.FromMinutes(31));
            using var replacement = cache.Lease("replacement", Guid.Empty, () => new ServiceCollection().BuildServiceProvider());
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
        cache.Remove(tenant.Uid.ToString());
        singleton.Verify(value => value.Dispose(), Times.Never);

        if (asynchronous) Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => scope.DisposeAsync().AsTask()));
        else Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => scope.Dispose()));
        await scope.DisposeAsync();

        tenantScope.Verify(value => value.Dispose(), Times.Once);
        hostScope.Verify(value => value.Dispose(), Times.Once);
        singleton.Verify(value => value.Dispose(), Times.Once);
    }
}
