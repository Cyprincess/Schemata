using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Foundation.State;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Schemata.Tenancy.Skeleton.Tenancy;
using Xunit;

namespace Schemata.Tenancy.Integration.Tests;

[Trait("Layer", "Integration")]
public sealed class TenantDataProtectionProviderShould
{
    private static readonly Guid TenantA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = new("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Fixed_Protectors_Stay_Bound_To_Their_Tenant_Under_Ambient_Switches() {
        var backend = new EphemeralDataProtectionProvider();
        var global = backend.CreateProtector("Schemata.Tenancy");
        var a = backend.CreateProtector("Schemata.Tenancy").CreateProtector(TenantA.ToString("N"));
        var b = backend.CreateProtector("Schemata.Tenancy").CreateProtector(TenantB.ToString("N"));

        var token = a.Protect(new byte[] { 1 });
        using (TenantContext.Enter(new(TenantB))) {
            Assert.Equal(new byte[] { 1 }, a.Unprotect(token));
            Assert.Throws<CryptographicException>(() => b.Unprotect(token));
            Assert.Throws<CryptographicException>(() => global.Unprotect(token));
        }
    }

    [Fact]
    public async Task State_Opt_In_Registers_Profile_And_Fixed_Provider_Without_Touching_The_Host() {
        var backend = new EphemeralDataProtectionProvider();
        var tenant = new SchemataTenant { Uid = TenantA, Timestamp = Guid.NewGuid() };
        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(value => value.FindByTenantId(TenantA, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        var services = new ServiceCollection();
        services.AddSingleton(manager.Object);
        services.AddSingleton<IDataProtectionProvider>(backend);
        new SchemataTenancyBuilder<SchemataTenant>(services).UseState(state => state.CookiePrefix = "tenant-a");
        await using var root = services.BuildServiceProvider();
        var tenancy = root.GetRequiredService<IOptions<SchemataTenancyOptions>>();
        var providers = new SchemataTenantServiceProviderFactory<SchemataTenant>(root, new MemoryCacheTenantProviderCache(tenancy), tenancy);

        await using var scope = await new SchemataTenantServiceScopeFactory<SchemataTenant>(root, providers).CreateAsync(new(tenant.Uid));
        var profile = scope.ServiceProvider.GetRequiredService<TenantStateProfile>();
        Assert.Equal(TenantA, profile.TenantUid);
        Assert.Equal("tenant-a", profile.CookiePrefix);

        var continuation = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("continuation");
        var token = continuation.Protect("cursor");
        using (TenantContext.Enter(new(TenantB))) {
            Assert.Equal("cursor", continuation.Unprotect(token));
            Assert.Throws<CryptographicException>(() => backend.CreateProtector("continuation").Unprotect(token));
            Assert.Throws<CryptographicException>(() => backend
                .CreateProtector("Schemata.Tenancy")
                .CreateProtector(TenantB.ToString("N"))
                .CreateProtector("continuation")
                .Unprotect(token));
        }
        Assert.Same(backend, root.GetRequiredService<IDataProtectionProvider>());
    }

    [Fact]
    public async Task State_Opt_In_Without_Host_Provider_Fails_At_Tenant_Container_Build() {
        var tenant = new SchemataTenant { Uid = TenantA, Timestamp = Guid.NewGuid() };
        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(value => value.FindByTenantId(TenantA, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        var services = new ServiceCollection();
        services.AddSingleton(manager.Object);
        new SchemataTenancyBuilder<SchemataTenant>(services).UseState(state => state.CookiePrefix = "tenant-a");
        await using var root = services.BuildServiceProvider();
        var tenancy = root.GetRequiredService<IOptions<SchemataTenancyOptions>>();
        var providers = new SchemataTenantServiceProviderFactory<SchemataTenant>(root, new MemoryCacheTenantProviderCache(tenancy), tenancy);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await providers.CreateServiceProviderAsync(TenantA));
    }

    [Fact]
    public async Task State_Opt_In_Without_Cookie_Prefix_Fails_At_Tenant_Container_Build() {
        var tenant = new SchemataTenant { Uid = TenantA, Timestamp = Guid.NewGuid() };
        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(value => value.FindByTenantId(TenantA, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        var services = new ServiceCollection();
        services.AddSingleton(manager.Object);
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        new SchemataTenancyBuilder<SchemataTenant>(services).UseState(_ => { });
        await using var root = services.BuildServiceProvider();
        var tenancy = root.GetRequiredService<IOptions<SchemataTenancyOptions>>();
        var providers = new SchemataTenantServiceProviderFactory<SchemataTenant>(root, new MemoryCacheTenantProviderCache(tenancy), tenancy);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await providers.CreateServiceProviderAsync(TenantA));
    }
}
