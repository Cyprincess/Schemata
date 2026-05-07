using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Schemata.Tenancy.Skeleton.Services;
using Xunit;

namespace Schemata.Tenancy.Tests;

public class SchemataTenantServiceProviderFactoryShould
{
    [Fact]
    public void Applies_Matching_TenantOverrides_To_Built_Container() {
        var options = new SchemataTenancyOptions();
        options.TenantOverrides[DeterministicGuid("alpha").ToString()] = [s => s.AddSingleton<IMarker, MarkerA>()];

        var factory = Build(options);
        var sp      = factory.CreateServiceProvider(AccessorFor("alpha"));

        Assert.IsType<MarkerA>(sp.GetRequiredService<IMarker>());
    }

    [Fact]
    public void Does_Not_Apply_TenantOverrides_For_Non_Matching_Id() {
        var options = new SchemataTenancyOptions();
        options.TenantOverrides[DeterministicGuid("alpha").ToString()] = [s => s.AddSingleton<IMarker, MarkerA>()];

        var factory = Build(options);
        var sp      = factory.CreateServiceProvider(AccessorFor("beta"));

        Assert.Null(sp.GetService<IMarker>());
    }

    [Fact]
    public void DynamicOverrides_Receive_Tenant_Id_And_Apply_To_Every_Container() {
        var seen    = new List<string>();
        var options = new SchemataTenancyOptions();
        options.DynamicOverrides.Add((id, sc, _) => {
                seen.Add(id);
                sc.AddSingleton<IMarker, MarkerA>();
            }
        );

        var factory = Build(options);
        var spA     = factory.CreateServiceProvider(AccessorFor("alpha"));
        var spB     = factory.CreateServiceProvider(AccessorFor("beta"));

        Assert.Equal([DeterministicGuid("alpha").ToString(), DeterministicGuid("beta").ToString()], seen);
        Assert.IsType<MarkerA>(spA.GetRequiredService<IMarker>());
        Assert.IsType<MarkerA>(spB.GetRequiredService<IMarker>());
    }

    [Fact]
    public void Overrides_Run_In_Order_AllOverrides_Tenant_Dynamic() {
        var services = new ServiceCollection();
        services.AddSingleton<IMarker, MarkerA>();

        var options = new SchemataTenancyOptions();
        options.TenantOverrides[DeterministicGuid("alpha").ToString()] = [s => s.AddSingleton<IMarker, MarkerB>()];
        options.DynamicOverrides.Add((_, s, _) => s.AddSingleton<IMarker, MarkerC>());

        var factory = Build(options, services);
        var sp      = factory.CreateServiceProvider(AccessorFor("alpha"));

        Assert.IsType<MarkerC>(sp.GetRequiredService<IMarker>());
    }

    [Fact]
    public void Null_Tenant_Id_Throws_TenantResolveException() {
        var factory = Build(new());

        var accessor = new Mock<ITenantContextAccessor<SchemataTenant>>();
        accessor.SetupGet(a => a.Tenant).Returns((SchemataTenant?)null);

        Assert.Throws<TenantResolveException>(() => factory.CreateServiceProvider(accessor.Object));
    }

    [Fact]
    public void Returns_Same_Provider_Instance_For_Same_Tenant_Id() {
        var factory = Build(new());
        var first   = factory.CreateServiceProvider(AccessorFor("alpha"));
        var second  = factory.CreateServiceProvider(AccessorFor("alpha"));

        Assert.Same(first, second);
    }

    private static SchemataTenantServiceProviderFactory<SchemataTenant> Build(
        SchemataTenancyOptions options,
        IServiceCollection?    services = null
    ) {
        services ??= new ServiceCollection();
        var cache = new MemoryCacheTenantProviderCache(
            Options.Create(new SchemataTenancyOptions { ProviderMaxCapacity = 1000 })
        );
        return new(services, new ServiceCollection().BuildServiceProvider(), cache, Options.Create(options));
    }

    private static ITenantContextAccessor<SchemataTenant> AccessorFor(string id) {
        var guid = DeterministicGuid(id);
        var mock = new Mock<ITenantContextAccessor<SchemataTenant>>();
        mock.SetupGet(a => a.Tenant).Returns(new SchemataTenant { Uid = guid });
        mock.Setup(a => a.InitializeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mock.Setup(a => a.InitializeAsync(It.IsAny<SchemataTenant>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(a => a.GetBaseServiceProviderAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceCollection().BuildServiceProvider());
        return mock.Object;
    }

    private static Guid DeterministicGuid(string label) {
        Span<byte> bytes = stackalloc byte[16];
        Encoding.ASCII.GetBytes(label.PadRight(16, '-'), bytes);
        return new(bytes);
    }

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
}
