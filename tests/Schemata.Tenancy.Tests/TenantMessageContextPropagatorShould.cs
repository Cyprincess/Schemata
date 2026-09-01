using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Tenancy.Foundation.Messaging;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Xunit;

namespace Schemata.Tenancy.Tests;

public class TenantMessageContextPropagatorShould
{
    [Fact]
    public async Task Capture_Then_RestoreAsync_RoundTrips_TheResolvedTenant() {
        var tenant = new SchemataTenant { Uid = Guid.NewGuid() };

        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(m => m.FindByTenantId(tenant.Uid, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);

        var root = BuildTenancyServices(manager.Object).BuildServiceProvider();

        // Source and target are independent scopes off the one root the feature configures, so each
        // gets its own scoped SchemataTenantContextAccessor<SchemataTenant> instance - exactly the
        // per-request shape SchemataTenancyFeature wires, not a hand-built singleton.
        using var sourceScope = root.CreateScope();
        var sourceInitializer = sourceScope.ServiceProvider.GetRequiredService<ITenantContextInitializer<SchemataTenant>>();
        await sourceInitializer.InitializeAsync(tenant, CancellationToken.None);

        var propagator = new TenantMessageContextPropagator<SchemataTenant>();
        var items       = new Dictionary<string, string?>();
        propagator.Capture(items, sourceScope.ServiceProvider);

        using var targetScope = root.CreateScope();
        await propagator.RestoreAsync(items, targetScope.ServiceProvider, CancellationToken.None);

        var targetAccessor = targetScope.ServiceProvider.GetRequiredService<ITenantContextAccessor<SchemataTenant>>();
        Assert.Same(tenant, targetAccessor.Tenant);
    }

    [Fact]
    public void Capture_NoOp_WhenNoTenantIsResolved() {
        var root = BuildTenancyServices(Mock.Of<ITenantManager<SchemataTenant>>()).BuildServiceProvider();
        using var scope = root.CreateScope();

        var propagator = new TenantMessageContextPropagator<SchemataTenant>();
        var items       = new Dictionary<string, string?>();
        propagator.Capture(items, scope.ServiceProvider);

        Assert.Empty(items);
    }

    [Fact]
    public async Task RestoreAsync_NoOp_WhenItemsCarryNoTenantIdentifier() {
        var manager = new Mock<ITenantManager<SchemataTenant>>(MockBehavior.Strict);
        var root     = BuildTenancyServices(manager.Object).BuildServiceProvider();
        using var scope = root.CreateScope();

        var propagator = new TenantMessageContextPropagator<SchemataTenant>();
        await propagator.RestoreAsync(new Dictionary<string, string?>(), scope.ServiceProvider, CancellationToken.None);

        manager.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RestoreAsync_Throws_TenantResolveException_WhenTheCapturedTenantNoLongerExists() {
        var tenantId = Guid.NewGuid();
        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(m => m.FindByTenantId(tenantId, It.IsAny<CancellationToken>())).ReturnsAsync((SchemataTenant?)null);

        var root = BuildTenancyServices(manager.Object).BuildServiceProvider();
        using var scope = root.CreateScope();

        var propagator = new TenantMessageContextPropagator<SchemataTenant>();
        var items       = new Dictionary<string, string?> { ["tenancy.tenant-id"] = tenantId.ToString("D") };

        await Assert.ThrowsAsync<TenantResolveException>(() => propagator.RestoreAsync(items, scope.ServiceProvider, CancellationToken.None).AsTask());
    }

    /// <summary>
    ///     Registers <see cref="SchemataTenantContextAccessor{TTenant}" /> scoped and forwards
    ///     <see cref="ITenantContextAccessor{TTenant}" />/<see cref="ITenantContextInitializer{TTenant}" />
    ///     as transient factories over it — the exact lifetimes <c>SchemataTenancyFeature.ConfigureServices</c>
    ///     registers — so tests exercise the real per-scope accessor/initializer mapping instead of
    ///     a hand-wired singleton.
    /// </summary>
    private static IServiceCollection BuildTenancyServices(ITenantManager<SchemataTenant> manager) {
        var services = new ServiceCollection();
        services.AddSingleton(manager);
        services.AddScoped<SchemataTenantContextAccessor<SchemataTenant>>(sp =>
            new(sp, Mock.Of<ITenantResolver>(), sp.GetRequiredService<ITenantManager<SchemataTenant>>()));
        services.AddTransient<ITenantContextAccessor<SchemataTenant>>(sp => sp.GetRequiredService<SchemataTenantContextAccessor<SchemataTenant>>());
        services.AddTransient<ITenantContextInitializer<SchemataTenant>>(sp => sp.GetRequiredService<SchemataTenantContextAccessor<SchemataTenant>>());

        return services;
    }
}
