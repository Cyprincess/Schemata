using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Xunit;

namespace Schemata.Tenancy.Tests;

public class SchemataTenantServiceProviderFactoryShould
{
    [Fact]
    public async Task Applies_Matching_TenantOverrides_To_Built_Container() {
        var options = new SchemataTenancyOptions { TenantOverrides = { [TenantId("alpha")] = [s => s.AddSingleton<IMarker, MarkerA>()] } };

        var       factory = Build("alpha", options);
        using var lease   = await factory.CreateServiceProviderAsync(UidFor("alpha"));

        Assert.IsType<MarkerA>(lease.Provider.GetRequiredService<IMarker>());
    }

    [Fact]
    public async Task Does_Not_Apply_TenantOverrides_For_Non_Matching_Id() {
        var options = new SchemataTenancyOptions { TenantOverrides = { [TenantId("alpha")] = [s => s.AddSingleton<IMarker, MarkerA>()] } };

        var       factory = Build("alpha", "beta", options);
        using var lease   = await factory.CreateServiceProviderAsync(UidFor("beta"));

        Assert.Null(lease.Provider.GetService<IMarker>());
    }

    [Fact]
    public async Task DynamicOverrides_Receive_Tenant_Id_And_Apply_To_Every_Container() {
        var seen    = new List<string>();
        var options = new SchemataTenancyOptions();
        options.DynamicOverrides.Add((id, sc, _) => {
            seen.Add(id);
            sc.AddSingleton<IMarker, MarkerA>();
        });

        var       factory = Build("alpha", "beta", options);
        using var leaseA  = await factory.CreateServiceProviderAsync(UidFor("alpha"));
        using var leaseB  = await factory.CreateServiceProviderAsync(UidFor("beta"));

        Assert.Equal([TenantId("alpha"), TenantId("beta")], seen);
        Assert.IsType<MarkerA>(leaseA.Provider.GetRequiredService<IMarker>());
        Assert.IsType<MarkerA>(leaseB.Provider.GetRequiredService<IMarker>());
    }

    [Fact]
    public async Task Overrides_Run_In_Order_Tenant_Then_Dynamic() {
        var services = new ServiceCollection();
        services.AddSingleton<IMarker, MarkerA>();

        var options = new SchemataTenancyOptions { TenantOverrides = { [TenantId("alpha")] = [s => s.AddSingleton<IMarker, MarkerB>()] } };
        options.DynamicOverrides.Add((_, s, _) => s.AddSingleton<IMarker, MarkerC>());

        var       factory = Build("alpha", options, services);
        using var lease   = await factory.CreateServiceProviderAsync(UidFor("alpha"));

        Assert.IsType<MarkerC>(lease.Provider.GetRequiredService<IMarker>());
    }

    [Fact]
    public async Task TenantOverride_ResolvesDependencyFromHostRoot() {
        var services = new ServiceCollection();
        services.AddSingleton<IDependency, RootDependency>();

        var options = new SchemataTenancyOptions { TenantOverrides = { [TenantId("alpha")] = [s => s.AddSingleton<IConsumer, TenantConsumer>()] } };

        var       factory = Build("alpha", options, services);
        using var lease   = await factory.CreateServiceProviderAsync(UidFor("alpha"));

        var consumer = Assert.IsType<TenantConsumer>(lease.Provider.GetRequiredService<IConsumer>());
        Assert.IsType<RootDependency>(consumer.Dependency);
    }

    [Fact]
    public async Task Enumerable_MergesHostAndTenantRegistrations() {
        var services = new ServiceCollection();
        services.AddSingleton<IMarker, MarkerA>();

        var options = new SchemataTenancyOptions { TenantOverrides = { [TenantId("alpha")] = [s => s.AddSingleton<IMarker, MarkerB>()] } };

        var       factory = Build("alpha", options, services);
        using var lease   = await factory.CreateServiceProviderAsync(UidFor("alpha"));

        var markers = lease.Provider.GetServices<IMarker>().ToList();
        Assert.Equal(2, markers.Count);
        Assert.Contains(markers, m => m is MarkerA);
        Assert.Contains(markers, m => m is MarkerB);
    }

    [Fact]
    public async Task Missing_Tenant_Throws_TenantResolveException() {
        var factory = Build("alpha");
        var unknown = Guid.NewGuid();

        await Assert.ThrowsAsync<TenantResolveException>(async () => await factory.CreateServiceProviderAsync(unknown));
    }

    [Fact]
    public async Task Returns_Same_Provider_Instance_For_Same_Tenant_Id() {
        var       factory = Build("alpha");
        using var first   = await factory.CreateServiceProviderAsync(UidFor("alpha"));
        using var second  = await factory.CreateServiceProviderAsync(UidFor("alpha"));

        Assert.Same(first.Provider, second.Provider);
    }

    [Fact]
    public async Task Cached_Provider_Does_Not_Pin_First_Request_Accessor() {
        var       factory = Build("alpha");
        using var first   = await factory.CreateServiceProviderAsync(UidFor("alpha"));
        using var second  = await factory.CreateServiceProviderAsync(UidFor("alpha"));

        Assert.Same(first.Provider, second.Provider);

        var resolved = second.Provider.GetRequiredService<ITenantContextAccessor<SchemataTenant>>();
        Assert.IsType<TenantBoundContextAccessor<SchemataTenant>>(resolved);
        Assert.Equal(UidFor("alpha"), resolved.Tenant!.Uid);
    }

    [Fact]
    public async Task Scoped_Override_Resolves_New_Instance_Per_Scope() {
        var services = new ServiceCollection();
        services.AddSingleton<IDependency, RootDependency>();

        var options = new SchemataTenancyOptions { TenantOverrides = { [TenantId("alpha")] = [s => s.AddScoped<IScopedMarker, ScopedMarker>()] } };

        var       factory = Build("alpha", options, services);
        using var lease   = await factory.CreateServiceProviderAsync(UidFor("alpha"));

        using var scopeA = lease.Provider.CreateScope();
        using var scopeB = lease.Provider.CreateScope();

        var fromA = scopeA.ServiceProvider.GetRequiredService<IScopedMarker>();
        var fromB = scopeB.ServiceProvider.GetRequiredService<IScopedMarker>();

        Assert.NotSame(fromA, fromB);
        Assert.IsType<RootDependency>(((ScopedMarker)fromA).Dependency);
        Assert.IsType<RootDependency>(((ScopedMarker)fromB).Dependency);
    }

    private static SchemataTenantServiceProviderFactory<SchemataTenant> Build(
        string                       primary,
        SchemataTenancyOptions?      options = null,
        IServiceCollection?          services = null
    ) {
        return Build(primary, primary, options, services);
    }

    private static SchemataTenantServiceProviderFactory<SchemataTenant> Build(
        string                       primary,
        string                       secondary,
        SchemataTenancyOptions?      options = null,
        IServiceCollection?          services = null
    ) {
        services ??= new ServiceCollection();
        options   ??= new SchemataTenancyOptions();
        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(m => m.FindByTenantId(UidFor(primary), It.IsAny<CancellationToken>())).ReturnsAsync(TenantFor(primary));
        manager.Setup(m => m.FindByTenantId(UidFor(secondary), It.IsAny<CancellationToken>())).ReturnsAsync(TenantFor(secondary));
        manager.Setup(m => m.FindByTenantId(It.Is<Guid>(g => g != UidFor(primary) && g != UidFor(secondary)), It.IsAny<CancellationToken>()))
               .ReturnsAsync((SchemataTenant?)null);
        services.AddSingleton(manager.Object);

        var root    = services.BuildServiceProvider();
        var cache   = new MemoryCacheTenantProviderCache(
            Options.Create(new SchemataTenancyOptions { ProviderMaxCapacity = 1000 }));
        return new(root, cache, Options.Create(options));
    }

    private static SchemataTenant TenantFor(string label) {
        return new() { Uid = UidFor(label), Timestamp = Guid.NewGuid() };
    }

    private static Guid UidFor(string label) {
        Span<byte> bytes = stackalloc byte[16];
        Encoding.ASCII.GetBytes(label.PadRight(16, '-'), bytes);
        return new(bytes);
    }

    private static string TenantId(string label) {
        return UidFor(label).ToString();
    }

    #region Nested type: IConsumer

    private interface IConsumer;

    #endregion

    #region Nested type: IDependency

    private interface IDependency;

    #endregion

    #region Nested type: IMarker

    private interface IMarker;

    #endregion

    #region Nested type: IScopedMarker

    private interface IScopedMarker;

    private sealed class ScopedMarker(IDependency dependency) : IScopedMarker
    {
        public IDependency Dependency { get; } = dependency;
    }

    #endregion

    #region Nested type: MarkerA

    private sealed class MarkerA : IMarker;

    #endregion

    #region Nested type: MarkerB

    private sealed class MarkerB : IMarker;

    #endregion

    #region Nested type: MarkerC

    private sealed class MarkerC : IMarker;

    #endregion

    #region Nested type: RootDependency

    private sealed class RootDependency : IDependency;

    #endregion

    #region Nested type: TenantConsumer

    private sealed class TenantConsumer(IDependency dependency) : IConsumer
    {
        public IDependency Dependency { get; } = dependency;
    }

    #endregion
}
