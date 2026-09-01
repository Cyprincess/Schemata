using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Foundation.Messaging;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Xunit;

namespace Schemata.Tenancy.Tests;

public class TenantActorTurnScopeFactoryShould
{
    private const string TenantIdKey = "tenancy.tenant-id";

    [Fact]
    public async Task CreateAsync_ResolvesTenantIdentity_InTheBootstrapScope_BeforeBuildingTheTurnScope() {
        var tenant = new SchemataTenant { Uid = Guid.NewGuid() };
        var disposalLog = new List<string>();
        var (root, providerFactory) = BuildRoot(tenant, disposalLog, tenantOnlyProvider: new ServiceCollection().BuildServiceProvider());

        var factory = new TenantActorTurnScopeFactory<SchemataTenant>(root.GetRequiredService<IServiceScopeFactory>());
        var context = new MessageContext(new Dictionary<string, string?> { [TenantIdKey] = tenant.Uid.ToString("D") });

        await using var scope = await factory.CreateAsync(context);

        // ITenantServiceProviderFactory.CreateServiceProvider(accessor) only runs once the tenant
        // is already resolved on the bootstrap scope's own accessor (phase 1 before phase 2).
        Assert.Same(tenant, providerFactory.ObservedTenant);
    }

    [Fact]
    public async Task CreateAsync_BuildsTheTurnScope_FromTheTenantIsolatedProvider() {
        var tenant = new SchemataTenant { Uid = Guid.NewGuid() };
        var disposalLog = new List<string>();
        var tenantOnlyProvider = new ServiceCollection().AddScoped<TenantOnlyMarker>().BuildServiceProvider();
        var (root, _) = BuildRoot(tenant, disposalLog, tenantOnlyProvider);

        var factory = new TenantActorTurnScopeFactory<SchemataTenant>(root.GetRequiredService<IServiceScopeFactory>());
        var context = new MessageContext(new Dictionary<string, string?> { [TenantIdKey] = tenant.Uid.ToString("D") });

        await using var scope = await factory.CreateAsync(context);

        // Only registered in the tenant-isolated provider, never in the host root: resolving it
        // proves the turn scope descends from the tenant provider, not a scope off the root.
        Assert.NotNull(scope.ServiceProvider.GetService<TenantOnlyMarker>());
    }

    [Fact]
    public async Task DisposeAsync_ReleasesTheFinalScope_BeforeTheBootstrapScope() {
        var tenant = new SchemataTenant { Uid = Guid.NewGuid() };
        var disposalLog = new List<string>();
        var (root, _) = BuildRoot(tenant, disposalLog, tenantOnlyProvider: new ServiceCollection().BuildServiceProvider());

        var factory = new TenantActorTurnScopeFactory<SchemataTenant>(root.GetRequiredService<IServiceScopeFactory>());
        var context = new MessageContext(new Dictionary<string, string?> { [TenantIdKey] = tenant.Uid.ToString("D") });

        var scope = await factory.CreateAsync(context);
        await scope.DisposeAsync();

        Assert.Equal(["final-lease", "bootstrap-probe"], disposalLog);
    }

    /// <summary>
    ///     Builds a root <see cref="IServiceProvider" /> wired the same way
    ///     <c>SchemataTenancyFeature</c> wires it, plus a <see cref="StubTenantServiceProviderFactory" />
    ///     that records the tenant the bootstrap scope resolved before phase 2 runs. The registered
    ///     <see cref="ITenantResolver" /> depends on <see cref="BootstrapProbe" />, so resolving
    ///     <c>SchemataTenantContextAccessor&lt;SchemataTenant&gt;</c> — which phase 1's tenant-only
    ///     restore does, via <see cref="ITenantContextInitializer{TTenant}" /> — constructs it inside
    ///     the bootstrap scope (and thus disposes it with that scope), letting its disposal be told
    ///     apart from the final scope's own.
    /// </summary>
    private static (IServiceProvider Root, StubTenantServiceProviderFactory ProviderFactory) BuildRoot(
        SchemataTenant tenant, List<string> disposalLog, IServiceProvider tenantOnlyProvider
    ) {
        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(m => m.FindByTenantId(tenant.Uid, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);

        var providerFactory = new StubTenantServiceProviderFactory(tenantOnlyProvider, disposalLog);

        var services = new ServiceCollection();
        services.AddSingleton(manager.Object);
        services.AddSingleton(disposalLog);
        services.AddScoped<BootstrapProbe>();
        services.AddScoped<ITenantResolver, NoOpTenantResolver>();
        services.AddScoped<SchemataTenantContextAccessor<SchemataTenant>>(sp =>
            new(sp, sp.GetRequiredService<ITenantResolver>(), sp.GetRequiredService<ITenantManager<SchemataTenant>>()));
        services.AddTransient<ITenantContextAccessor<SchemataTenant>>(sp => sp.GetRequiredService<SchemataTenantContextAccessor<SchemataTenant>>());
        services.AddTransient<ITenantContextInitializer<SchemataTenant>>(sp => sp.GetRequiredService<SchemataTenantContextAccessor<SchemataTenant>>());
        services.AddScoped<TenantMessageContextPropagator<SchemataTenant>>();
        services.AddTransient<IMessageContextPropagator>(sp => sp.GetRequiredService<TenantMessageContextPropagator<SchemataTenant>>());
        services.AddScoped<ITenantServiceScopeFactory<SchemataTenant>>(sp =>
            new SchemataTenantServiceScopeFactory<SchemataTenant>(sp, sp.GetRequiredService<ITenantContextAccessor<SchemataTenant>>(), providerFactory));

        return (services.BuildServiceProvider(), providerFactory);
    }

    private sealed class TenantOnlyMarker;

    /// <summary>Disposed when the bootstrap scope is disposed — reached via <see cref="NoOpTenantResolver" />'s dependency on it.</summary>
    private sealed class BootstrapProbe(List<string> disposalLog) : IDisposable
    {
        public void Dispose() => disposalLog.Add("bootstrap-probe");
    }

    /// <summary>
    ///     A resolver that never actually resolves anything (phase 1 always calls
    ///     <c>ITenantContextInitializer&lt;TTenant&gt;.InitializeAsync(TTenant, ct)</c> directly, never
    ///     the parameterless overload that would use this). Its only purpose is depending on
    ///     <see cref="BootstrapProbe" />, so constructing <c>SchemataTenantContextAccessor&lt;TTenant&gt;</c>
    ///     — an ordinary, real part of phase 1 — forces <see cref="BootstrapProbe" /> into the bootstrap scope.
    /// </summary>
    private sealed class NoOpTenantResolver : ITenantResolver
    {
        public NoOpTenantResolver(BootstrapProbe probe) { }

        public Task<Guid?> ResolveAsync(CancellationToken ct) => Task.FromResult<Guid?>(null);
    }

    private sealed class StubTenantServiceProviderFactory(IServiceProvider tenantProvider, List<string> disposalLog)
        : ITenantServiceProviderFactory<SchemataTenant>
    {
        public SchemataTenant? ObservedTenant { get; private set; }

        public ITenantProviderLease CreateServiceProvider(ITenantContextAccessor<SchemataTenant> accessor) {
            ObservedTenant = accessor.Tenant;

            return new StubLease(tenantProvider, disposalLog);
        }
    }

    private sealed class StubLease(IServiceProvider provider, List<string> disposalLog) : ITenantProviderLease
    {
        public IServiceProvider Provider => provider;

        public void Dispose() => disposalLog.Add("final-lease");
    }
}
