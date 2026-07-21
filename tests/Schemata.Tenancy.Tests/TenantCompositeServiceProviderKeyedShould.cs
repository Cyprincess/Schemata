using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Xunit;

namespace Schemata.Tenancy.Tests;

public class TenantCompositeServiceProviderKeyedShould
{
    [Fact]
    public void Keyed_Overrides_Win_And_Root_Keys_Fall_Back_With_Composite_Factories() {
        const string id = "alpha";
        var services = new ServiceCollection();
        services.AddSingleton<IRootDependency, RootDependency>();
        services.AddKeyedSingleton<IKeyedMarker, RootKeyedMarker>("overridden");
        services.AddKeyedSingleton<IKeyedMarker, RootKeyedMarker>("root-only");

        var options = new SchemataTenancyOptions();
        options.TenantOverrides[TenantId(id)] = [s => {
            s.AddKeyedSingleton<IKeyedMarker, TenantKeyedMarker>("overridden");
            s.AddKeyedSingleton<IKeyedConsumer, KeyedTypeConsumer>("type");
            s.AddKeyedSingleton<IKeyedConsumer>("factory", (provider, _) => new KeyedFactoryConsumer(provider.GetRequiredService<IRootDependency>()));
        }];

        using var root  = services.BuildServiceProvider();
        using var cache = BuildCache();
        var       factory = BuildFactory(root, cache, options);
        using var lease   = factory.CreateServiceProvider(AccessorFor(id));
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
    public void Composite_And_Scopes_Return_Themselves_For_Di_Interfaces_And_Probe_Both_Containers() {
        const string id = "alpha";
        var services = new ServiceCollection();
        services.AddSingleton<IRootDependency, RootDependency>();
        services.AddKeyedSingleton<IKeyedMarker, RootKeyedMarker>("root");

        var options = new SchemataTenancyOptions();
        options.TenantOverrides[TenantId(id)] = [s => {
            s.AddSingleton<CompositeProbe>();
            s.AddKeyedSingleton<IKeyedMarker, TenantKeyedMarker>("tenant");
        }];

        using var root  = services.BuildServiceProvider();
        using var cache = BuildCache();
        var       factory = BuildFactory(root, cache, options);
        using var lease   = factory.CreateServiceProvider(AccessorFor(id));

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
    public void Final_Descriptor_Collection_Is_Wrapped_After_Insert_And_Replace() {
        const string id = "alpha";
        var services = new ServiceCollection();
        services.AddSingleton<IRootDependency, RootDependency>();

        var options = new SchemataTenancyOptions();
        options.TenantOverrides[TenantId(id)] = [s => {
            s.Insert(0, ServiceDescriptor.Singleton<IInsertedConsumer, InsertedConsumer>());
            s.AddSingleton<IReplacementConsumer, InitialReplacementConsumer>();
            s.Replace(ServiceDescriptor.Singleton<IReplacementConsumer, ReplacementConsumer>());
        }];

        using var root  = services.BuildServiceProvider();
        using var cache = BuildCache();
        var       factory = BuildFactory(root, cache, options);
        using var lease   = factory.CreateServiceProvider(AccessorFor(id));

        Assert.IsType<RootDependency>(lease.Provider.GetRequiredService<IInsertedConsumer>().Dependency);
        Assert.IsType<ReplacementConsumer>(lease.Provider.GetRequiredService<IReplacementConsumer>());
        Assert.IsType<RootDependency>(lease.Provider.GetRequiredService<IReplacementConsumer>().Dependency);
    }

    [Fact]
    public void Open_Generic_Override_Is_Rejected_When_Building_Tenant_Container() {
        const string id = "alpha";
        var options = new SchemataTenancyOptions();
        options.TenantOverrides[TenantId(id)] = [s => s.AddSingleton(typeof(IGenericMarker<>), typeof(GenericMarker<>))];

        using var root  = new ServiceCollection().BuildServiceProvider();
        using var cache = BuildCache();
        var       factory = BuildFactory(root, cache, options);

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateServiceProvider(AccessorFor(id)));
        Assert.Contains("open-generic", error.Message);
    }

    [Fact]
    public async Task Leased_Tenant_Scope_Forwards_Asynchronous_Disposal_To_Its_Composite_And_Lease() {
        const string id = "alpha";
        var singleton = new Mock<IAsyncDisposable>();
        singleton.Setup(disposable => disposable.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var options = new SchemataTenancyOptions();
        options.TenantOverrides[TenantId(id)] = [s => s.AddSingleton<IAsyncDisposable>(_ => singleton.Object)];

        using var root = new ServiceCollection().BuildServiceProvider();
        await using var cache = BuildCache();
        var factory = BuildFactory(root, cache, options);
        var accessor = AccessorFor(id);
        var scopes = new SchemataTenantServiceScopeFactory<SchemataTenant>(root, accessor, factory);
        var scope = scopes.CreateScope();
        _ = scope.ServiceProvider.GetRequiredService<IAsyncDisposable>();

        cache.Remove(TenantId(id));
        await ((IAsyncDisposable)scope).DisposeAsync();

        singleton.Verify(disposable => disposable.DisposeAsync(), Times.Once);
    }

    private static MemoryCacheTenantProviderCache BuildCache() {
        return new(Options.Create(new SchemataTenancyOptions { ProviderMaxCapacity = 10 }));
    }

    private static SchemataTenantServiceProviderFactory<SchemataTenant> BuildFactory(
        IServiceProvider                root,
        ITenantProviderCache            cache,
        SchemataTenancyOptions options
    ) {
        return new(root, cache, Options.Create(options));
    }

    private static ITenantContextAccessor<SchemataTenant> AccessorFor(string id) {
        var accessor = new Mock<ITenantContextAccessor<SchemataTenant>>();
        accessor.SetupGet(a => a.Tenant).Returns(new SchemataTenant { Uid = Guid.Parse(TenantId(id)) });
        return accessor.Object;
    }

    private static string TenantId(string id) {
        Span<byte> bytes = stackalloc byte[16];
        System.Text.Encoding.ASCII.GetBytes(id.PadRight(16, '-'), bytes);
        return new Guid(bytes).ToString();
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
