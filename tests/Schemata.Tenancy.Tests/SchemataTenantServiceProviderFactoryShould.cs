using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
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
    public void Applies_Matching_TenantOverrides_To_Built_Container() {
        var options = new SchemataTenancyOptions();
        options.TenantOverrides[DeterministicGuid("alpha").ToString()] = [s => s.AddSingleton<IMarker, MarkerA>()];

        var       factory = Build(options);
        using var lease   = factory.CreateServiceProvider(AccessorFor("alpha"));

        Assert.IsType<MarkerA>(lease.Provider.GetRequiredService<IMarker>());
    }

    [Fact]
    public void Does_Not_Apply_TenantOverrides_For_Non_Matching_Id() {
        var options = new SchemataTenancyOptions();
        options.TenantOverrides[DeterministicGuid("alpha").ToString()] = [s => s.AddSingleton<IMarker, MarkerA>()];

        var       factory = Build(options);
        using var lease   = factory.CreateServiceProvider(AccessorFor("beta"));

        Assert.Null(lease.Provider.GetService<IMarker>());
    }

    [Fact]
    public void DynamicOverrides_Receive_Tenant_Id_And_Apply_To_Every_Container() {
        var seen    = new List<string>();
        var options = new SchemataTenancyOptions();
        options.DynamicOverrides.Add((id, sc, _) => {
            seen.Add(id);
            sc.AddSingleton<IMarker, MarkerA>();
        });

        var       factory = Build(options);
        using var leaseA  = factory.CreateServiceProvider(AccessorFor("alpha"));
        using var leaseB  = factory.CreateServiceProvider(AccessorFor("beta"));

        Assert.Equal([DeterministicGuid("alpha").ToString(), DeterministicGuid("beta").ToString()], seen);
        Assert.IsType<MarkerA>(leaseA.Provider.GetRequiredService<IMarker>());
        Assert.IsType<MarkerA>(leaseB.Provider.GetRequiredService<IMarker>());
    }

    [Fact]
    public void Overrides_Run_In_Order_Tenant_Then_Dynamic() {
        var services = new ServiceCollection();
        services.AddSingleton<IMarker, MarkerA>();

        var options = new SchemataTenancyOptions();
        options.TenantOverrides[DeterministicGuid("alpha").ToString()] = [s => s.AddSingleton<IMarker, MarkerB>()];
        options.DynamicOverrides.Add((_, s, _) => s.AddSingleton<IMarker, MarkerC>());

        var       factory = Build(options, services);
        using var lease   = factory.CreateServiceProvider(AccessorFor("alpha"));

        Assert.IsType<MarkerC>(lease.Provider.GetRequiredService<IMarker>());
    }

    [Fact]
    public void TenantOverride_ResolvesDependencyFromHostRoot() {
        var services = new ServiceCollection();
        services.AddSingleton<IDependency, RootDependency>();

        var options = new SchemataTenancyOptions();
        options.TenantOverrides[DeterministicGuid("alpha").ToString()]
            = [s => s.AddSingleton<IConsumer, TenantConsumer>()];

        var       factory = Build(options, services);
        using var lease   = factory.CreateServiceProvider(AccessorFor("alpha"));

        // The tenant override's constructor dependency is satisfied from the host root.
        var consumer = Assert.IsType<TenantConsumer>(lease.Provider.GetRequiredService<IConsumer>());
        Assert.IsType<RootDependency>(consumer.Dependency);
    }

    [Fact]
    public void Enumerable_MergesHostAndTenantRegistrations() {
        var services = new ServiceCollection();
        services.AddSingleton<IMarker, MarkerA>();

        var options = new SchemataTenancyOptions();
        options.TenantOverrides[DeterministicGuid("alpha").ToString()] = [s => s.AddSingleton<IMarker, MarkerB>()];

        var       factory = Build(options, services);
        using var lease   = factory.CreateServiceProvider(AccessorFor("alpha"));

        // IEnumerable resolution sees both the host registration and the tenant addition.
        var markers = lease.Provider.GetServices<IMarker>().ToList();
        Assert.Equal(2, markers.Count);
        Assert.Contains(markers, m => m is MarkerA);
        Assert.Contains(markers, m => m is MarkerB);
    }

    [Fact]
    public void Null_Tenant_Throws_TenantResolveException() {
        var factory = Build(new());

        var accessor = new Mock<ITenantContextAccessor<SchemataTenant>>();
        accessor.SetupGet(a => a.Tenant).Returns((SchemataTenant?)null);

        Assert.Throws<TenantResolveException>(() => factory.CreateServiceProvider(accessor.Object));
    }

    [Fact]
    public void Returns_Same_Provider_Instance_For_Same_Tenant_Id() {
        var       factory = Build(new());
        using var first   = factory.CreateServiceProvider(AccessorFor("alpha"));
        using var second  = factory.CreateServiceProvider(AccessorFor("alpha"));

        Assert.Same(first.Provider, second.Provider);
    }

    [Fact]
    public void Cached_Provider_Does_Not_Pin_First_Request_Accessor() {
        var       factory       = Build(new());
        var       firstAccessor = AccessorFor("alpha");
        using var first         = factory.CreateServiceProvider(firstAccessor);

        var       secondAccessor = AccessorFor("alpha");
        using var second         = factory.CreateServiceProvider(secondAccessor);

        Assert.Same(first.Provider, second.Provider);

        // Inside the cached provider, ITenantContextAccessor<TTenant> resolves to a
        // tenant-bound implementation tied to the tenant id.
        var resolved = second.Provider.GetRequiredService<ITenantContextAccessor<SchemataTenant>>();
        Assert.IsType<TenantBoundContextAccessor<SchemataTenant>>(resolved);
        Assert.NotSame(firstAccessor, resolved);
        Assert.NotSame(secondAccessor, resolved);
        Assert.Equal(DeterministicGuid("alpha"), resolved.Tenant!.Uid);
    }

    private static SchemataTenantServiceProviderFactory<SchemataTenant> Build(
        SchemataTenancyOptions options,
        IServiceCollection?    services = null
    ) {
        var root = (services ?? new ServiceCollection()).BuildServiceProvider();
        var cache = new MemoryCacheTenantProviderCache(
            Options.Create(new SchemataTenancyOptions { ProviderMaxCapacity = 1000 }));
        return new(root, cache, Options.Create(options));
    }

    private static ITenantContextAccessor<SchemataTenant> AccessorFor(string id) {
        var guid = DeterministicGuid(id);
        var mock = new Mock<ITenantContextAccessor<SchemataTenant>>();
        mock.SetupGet(a => a.Tenant).Returns(new SchemataTenant { Uid = guid });
        mock.Setup(a => a.GetBaseServiceProviderAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceCollection().BuildServiceProvider());
        return mock.Object;
    }

    private static Guid DeterministicGuid(string label) {
        Span<byte> bytes = stackalloc byte[16];
        Encoding.ASCII.GetBytes(label.PadRight(16, '-'), bytes);
        return new(bytes);
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
