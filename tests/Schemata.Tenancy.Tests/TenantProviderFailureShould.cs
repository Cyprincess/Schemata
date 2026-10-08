using System;
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

public class TenantProviderFailureShould
{
    [Fact]
    public async Task Bootstrap_Disposal_Failure_Does_Not_Acquire_A_Provider_Lease() {
        var tenant = new SchemataTenant { Uid = Guid.NewGuid(), Timestamp = Guid.NewGuid() };
        var failure = new InvalidOperationException("bootstrap cleanup");
        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(value => value.FindByTenantId(tenant.Uid, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        manager.As<IAsyncDisposable>().Setup(value => value.DisposeAsync()).Throws(failure);
        await using var root = new ServiceCollection().AddScoped(_ => manager.Object).BuildServiceProvider();
        var cache = new Mock<ITenantProviderCache>(MockBehavior.Strict);
        var factory = new SchemataTenantServiceProviderFactory<SchemataTenant>(root, cache.Object, Options.Create(new SchemataTenancyOptions()));
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => factory.CreateServiceProviderAsync(tenant.Uid).AsTask()));
        cache.Verify(value => value.Lease(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Func<IServiceProvider>>()), Times.Never);
    }

    [Fact]
    [Trait("Layer", "Integration")]
    public async Task Override_Source_Failure_Allows_A_Fresh_Provider_And_Lease_Cleanup() {
        var tenant = new SchemataTenant { Uid = Guid.NewGuid(), Timestamp = Guid.NewGuid() };
        var failure = new InvalidOperationException("override source");
        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(value => value.FindByTenantId(tenant.Uid, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        await using var root = new ServiceCollection().AddSingleton(manager.Object).BuildServiceProvider();
        using var cache = new MemoryCacheTenantProviderCache(Options.Create(new SchemataTenancyOptions()));
        var singleton = new Mock<IDisposable>();
        var options = new SchemataTenancyOptions();
        options.DynamicOverrides.Add((_, _, _) => throw failure);
        var factory = new SchemataTenantServiceProviderFactory<SchemataTenant>(root, cache, Options.Create(options));

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => factory.CreateServiceProviderAsync(tenant.Uid).AsTask()));

        options.DynamicOverrides.Clear();
        options.DynamicOverrides.Add((_, services, _) => services.AddSingleton<IDisposable>(_ => singleton.Object));
        var lease = await factory.CreateServiceProviderAsync(tenant.Uid);
        Assert.Same(tenant, lease.Provider.GetRequiredService<SchemataTenant>());
        Assert.Same(singleton.Object, lease.Provider.GetRequiredService<IDisposable>());
        cache.Remove(tenant.Uid.ToString());
        singleton.Verify(value => value.Dispose(), Times.Never);
        lease.Dispose();
        lease.Dispose();
        singleton.Verify(value => value.Dispose(), Times.Once);
    }
}
