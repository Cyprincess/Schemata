using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Tenancy;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Xunit;

namespace Schemata.Tenancy.Tests;

public class TenantCompositeServiceProviderKeyedShould
{
    [Fact]
    public async Task Keyed_Overrides_Win_And_Root_Keys_Fall_Back_With_Composite_Factories() {
        const string id = "alpha";
        var services = new ServiceCollection();
        services.AddSingleton<IRootDependency, RootDependency>();
        services.AddKeyedSingleton<IKeyedMarker, RootKeyedMarker>("overridden");
        services.AddKeyedSingleton<IKeyedMarker, RootKeyedMarker>("root-only");

        var options = new SchemataTenancyOptions { TenantOverrides = { [TenantId(id)] = [s => {
                s.AddKeyedSingleton<IKeyedMarker, TenantKeyedMarker>("overridden");
                s.AddKeyedSingleton<IKeyedConsumer, KeyedTypeConsumer>("type");
                s.AddKeyedSingleton<IKeyedConsumer>("factory", (provider, _) => new KeyedFactoryConsumer(provider.GetRequiredService<IRootDependency>()));
            }],
        } };

        using var root    = RegisterTenantManager(services, id).BuildServiceProvider();
        using var cache   = new MemoryCacheTenantProviderCache(Options.Create(new SchemataTenancyOptions { ProviderMaxCapacity = 10 }));
        var       factory = new SchemataTenantServiceProviderFactory<SchemataTenant>(root, cache, Options.Create(options));
        using var lease   = await factory.CreateServiceProviderAsync(UidFor(id));
        var keyed = (IKeyedServiceProvider)lease.Provider;

        Assert.IsType<TenantKeyedMarker>(keyed.GetRequiredKeyedService(typeof(IKeyedMarker), "overridden"));
        Assert.IsType<RootKeyedMarker>(keyed.GetRequiredKeyedService(typeof(IKeyedMarker), "root-only"));
        Assert.IsType<RootDependency>(((IKeyedConsumer)keyed.GetRequiredKeyedService(typeof(IKeyedConsumer), "type")).Dependency);
        Assert.IsType<RootDependency>(((IKeyedConsumer)keyed.GetRequiredKeyedService(typeof(IKeyedConsumer), "factory")).Dependency);

        var overridden = ((IEnumerable<IKeyedMarker>)keyed.GetRequiredKeyedService(typeof(IEnumerable<IKeyedMarker>), "overridden")).ToArray();
        var rootOnly = ((IEnumerable<IKeyedMarker>)keyed.GetRequiredKeyedService(typeof(IEnumerable<IKeyedMarker>), "root-only")).ToArray();
        Assert.Collection(overridden, marker => Assert.IsType<TenantKeyedMarker>(marker));
        Assert.Collection(rootOnly, marker => Assert.IsType<RootKeyedMarker>(marker));
    }

    [Fact]
    public async Task Composite_And_Scopes_Return_Themselves_For_Di_Interfaces_And_Probe_Both_Containers() {
        const string id = "alpha";
        var services = new ServiceCollection();
        services.AddSingleton<IRootDependency, RootDependency>();
        services.AddKeyedSingleton<IKeyedMarker, RootKeyedMarker>("root");

        var options = new SchemataTenancyOptions { TenantOverrides = { [TenantId(id)] = [s => {
                s.AddSingleton<CompositeProbe>();
                s.AddKeyedSingleton<IKeyedMarker, TenantKeyedMarker>("tenant");
            }],
        } };

        using var root    = RegisterTenantManager(services, id).BuildServiceProvider();
        using var cache   = new MemoryCacheTenantProviderCache(Options.Create(new SchemataTenancyOptions { ProviderMaxCapacity = 10 }));
        var       factory = new SchemataTenantServiceProviderFactory<SchemataTenant>(root, cache, Options.Create(options));
        using var lease   = await factory.CreateServiceProviderAsync(UidFor(id));

        var probe = lease.Provider.GetRequiredService<CompositeProbe>();
        Assert.Same(lease.Provider, probe.Provider);
        Assert.Same(lease.Provider, probe.ScopeFactory);
        Assert.Same(lease.Provider, probe.KeyedProvider);
        Assert.Same(lease.Provider, probe.ServiceProbe);
        Assert.Same(lease.Provider, probe.KeyedProbe);
        Assert.True(probe.ServiceProbe.IsService(typeof(CompositeProbe)));
        Assert.True(probe.ServiceProbe.IsService(typeof(IRootDependency)));
        Assert.True(probe.KeyedProbe.IsKeyedService(typeof(IKeyedMarker), "tenant"));
        Assert.True(probe.KeyedProbe.IsKeyedService(typeof(IKeyedMarker), "root"));

        using var scope = lease.Provider.CreateScope();
        var scoped = scope.ServiceProvider;
        Assert.Same(scoped, scoped.GetRequiredService<IServiceProvider>());
        Assert.Same(scoped, scoped.GetRequiredService<IServiceScopeFactory>());
        Assert.Same(scoped, scoped.GetRequiredService<IKeyedServiceProvider>());
        Assert.Same(scoped, scoped.GetRequiredService<IServiceProviderIsService>());
        Assert.Same(scoped, scoped.GetRequiredService<IServiceProviderIsKeyedService>());
        Assert.True(((IServiceProviderIsService)scoped).IsService(typeof(IRootDependency)));
        Assert.True(((IServiceProviderIsKeyedService)scoped).IsKeyedService(typeof(IKeyedMarker), "tenant"));
    }

    [Fact]
    public async Task Final_Descriptor_Collection_Is_Wrapped_After_Insert_And_Replace() {
        const string id = "alpha";
        var services = new ServiceCollection();
        services.AddSingleton<IRootDependency, RootDependency>();

        var options = new SchemataTenancyOptions { TenantOverrides = { [TenantId(id)] = [s => {
                s.Insert(0, ServiceDescriptor.Singleton<IInsertedConsumer, InsertedConsumer>());
                s.AddSingleton<IReplacementConsumer, InitialReplacementConsumer>();
                s.Replace(ServiceDescriptor.Singleton<IReplacementConsumer, ReplacementConsumer>());
            }],
        } };

        using var root    = RegisterTenantManager(services, id).BuildServiceProvider();
        using var cache   = new MemoryCacheTenantProviderCache(Options.Create(new SchemataTenancyOptions { ProviderMaxCapacity = 10 }));
        var       factory = new SchemataTenantServiceProviderFactory<SchemataTenant>(root, cache, Options.Create(options));
        using var lease   = await factory.CreateServiceProviderAsync(UidFor(id));

        Assert.IsType<RootDependency>(lease.Provider.GetRequiredService<IInsertedConsumer>().Dependency);
        Assert.IsType<ReplacementConsumer>(lease.Provider.GetRequiredService<IReplacementConsumer>());
        Assert.IsType<RootDependency>(lease.Provider.GetRequiredService<IReplacementConsumer>().Dependency);
    }

    [Fact]
    public async Task Open_Generic_Override_Is_Rejected_When_Building_Tenant_Container() {
        const string id      = "alpha";
        var          options = new SchemataTenancyOptions { TenantOverrides = { [TenantId(id)] = [s => s.AddSingleton(typeof(IGenericMarker<>), typeof(GenericMarker<>))] } };

        using var root    = RegisterTenantManager(new ServiceCollection(), id).BuildServiceProvider();
        using var cache   = new MemoryCacheTenantProviderCache(Options.Create(new SchemataTenancyOptions { ProviderMaxCapacity = 10 }));
        var       factory = new SchemataTenantServiceProviderFactory<SchemataTenant>(root, cache, Options.Create(options));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await factory.CreateServiceProviderAsync(UidFor(id)));
        Assert.Contains("open-generic", error.Message);
    }

    [Fact]
    public async Task Leased_Tenant_Scope_Forwards_Asynchronous_Disposal_To_Its_Composite_And_Lease() {
        const string id = "alpha";
        var singleton = new Mock<IAsyncDisposable>();
        singleton.Setup(disposable => disposable.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var options = new SchemataTenancyOptions { TenantOverrides = { [TenantId(id)] = [s => s.AddSingleton<IAsyncDisposable>(_ => singleton.Object)] } };

        using var root    = RegisterTenantManager(new ServiceCollection(), id).BuildServiceProvider();
        await using var cache   = new MemoryCacheTenantProviderCache(Options.Create(new SchemataTenancyOptions { ProviderMaxCapacity = 10 }));
        var factory = new SchemataTenantServiceProviderFactory<SchemataTenant>(root, cache, Options.Create(options));
        var scopes  = new SchemataTenantServiceScopeFactory<SchemataTenant>(root, factory);
        var scope   = await scopes.CreateAsync(new TenantIdentity(UidFor(id)));
        _ = scope.ServiceProvider.GetRequiredService<IAsyncDisposable>();

        cache.Remove(TenantId(id));
        await ((IAsyncDisposable)scope).DisposeAsync();

        singleton.Verify(disposable => disposable.DisposeAsync(), Times.Once);
    }

    private static IServiceCollection RegisterTenantManager(IServiceCollection services, string id) {
        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(m => m.FindByTenantId(UidFor(id), It.IsAny<CancellationToken>())).ReturnsAsync(TenantFor(id));
        services.AddSingleton(manager.Object);
        return services;
    }

    private static SchemataTenant TenantFor(string label) {
        return new() { Uid = UidFor(label), Timestamp = Guid.NewGuid() };
    }

    private static Guid UidFor(string label) {
        Span<byte> bytes = stackalloc byte[16];
        System.Text.Encoding.ASCII.GetBytes(label.PadRight(16, '-'), bytes);
        return new(bytes);
    }

    private static string TenantId(string label) {
        return UidFor(label).ToString();
    }

    private interface IGenericMarker<T>;

    private interface IInsertedConsumer
    {
        IRootDependency Dependency { get; }
    }

    private interface IKeyedConsumer
    {
        IRootDependency Dependency { get; }
    }

    private interface IKeyedMarker;

    private interface IReplacementConsumer
    {
        IRootDependency Dependency { get; }
    }

    private interface IRootDependency;

    private sealed class CompositeProbe(
        IServiceProvider              provider,
        IServiceScopeFactory          scopeFactory,
        IKeyedServiceProvider         keyedProvider,
        IServiceProviderIsService     serviceProbe,
        IServiceProviderIsKeyedService keyedProbe
    )
    {
        public IKeyedServiceProvider          KeyedProvider { get; } = keyedProvider;
        public IServiceProviderIsKeyedService KeyedProbe    { get; } = keyedProbe;
        public IServiceProvider               Provider      { get; } = provider;
        public IServiceProviderIsService      ServiceProbe  { get; } = serviceProbe;
        public IServiceScopeFactory           ScopeFactory  { get; } = scopeFactory;
    }

    private sealed class GenericMarker<T> : IGenericMarker<T>;

    private sealed class InitialReplacementConsumer(IRootDependency dependency) : IReplacementConsumer
    {
        public IRootDependency Dependency { get; } = dependency;
    }

    private sealed class InsertedConsumer(IRootDependency dependency) : IInsertedConsumer
    {
        public IRootDependency Dependency { get; } = dependency;
    }

    private sealed class KeyedFactoryConsumer(IRootDependency dependency) : IKeyedConsumer
    {
        public IRootDependency Dependency { get; } = dependency;
    }

    private sealed class KeyedTypeConsumer(IRootDependency dependency) : IKeyedConsumer
    {
        public IRootDependency Dependency { get; } = dependency;
    }

    private sealed class ReplacementConsumer(IRootDependency dependency) : IReplacementConsumer
    {
        public IRootDependency Dependency { get; } = dependency;
    }

    private sealed class RootDependency : IRootDependency;

    private sealed class RootKeyedMarker : IKeyedMarker;

    private sealed class TenantKeyedMarker : IKeyedMarker;
}
