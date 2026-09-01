using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Foundation.Features;
using Schemata.Tenancy.Foundation.Messaging;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Xunit;

namespace Schemata.Tenancy.Tests;

public class SchemataTenancyFeatureShould
{
    [Fact]
    public void ConfigureServices_DoesNotThrow_WhenRegisteringTheTenantPropagator() {
        var services = new ServiceCollection();
        var feature  = new SchemataTenancyFeature<NoOpTenantManager, SchemataTenant>();

        // TryAddEnumerable rejects a factory-based descriptor whose inferred implementation type
        // equals its service type (IMessageContextPropagator); this call is where that check runs,
        // synchronously, at registration time - before a provider is ever built.
        var exception = Record.Exception(() =>
            feature.ConfigureServices(services, new SchemataOptions(), new Configurators(), new ConfigurationBuilder().Build(), environment: null!));

        Assert.Null(exception);
    }

    [Fact]
    public void ConfigureServices_MakesThePropagator_ResolvableBothAsTheInterfaceCollectionAndTheConcreteType() {
        var services = new ServiceCollection();
        var feature  = new SchemataTenancyFeature<NoOpTenantManager, SchemataTenant>();
        feature.ConfigureServices(services, new SchemataOptions(), new Configurators(), new ConfigurationBuilder().Build(), environment: null!);

        using var provider = services.BuildServiceProvider();
        using var scope     = provider.CreateScope();

        var concrete = scope.ServiceProvider.GetRequiredService<TenantMessageContextPropagator<SchemataTenant>>();
        var enumerable = scope.ServiceProvider.GetServices<IMessageContextPropagator>();

        var fromCollection = Assert.Single(enumerable);
        Assert.Same(concrete, fromCollection);
    }

    /// <summary>
    ///     Satisfies <c>SchemataTenancyFeature{TManager,TTenant}</c>'s <c>TManager</c> constraint.
    ///     Every member throws: nothing in these facts resolves <see cref="ITenantManager{TTenant}" />,
    ///     only registers it, so this type is never actually constructed or invoked.
    /// </summary>
    private sealed class NoOpTenantManager : ITenantManager<SchemataTenant>
    {
        public ValueTask<SchemataTenant?> FindByTenantId(Guid identifier, CancellationToken ct) => throw new NotImplementedException();

        public ValueTask<SchemataTenant?> FindByHost(string host, CancellationToken ct) => throw new NotImplementedException();

        public ValueTask<System.Collections.Immutable.ImmutableArray<string>> GetHostsAsync(SchemataTenant tenant, CancellationToken ct) =>
            throw new NotImplementedException();

        public ValueTask SetDisplayNameAsync(SchemataTenant tenant, string? name, CancellationToken ct) => throw new NotImplementedException();

        public ValueTask SetDisplayNamesAsync(SchemataTenant tenant, Dictionary<string, string?> names, CancellationToken ct) =>
            throw new NotImplementedException();

        public ValueTask SetHostsAsync(SchemataTenant tenant, System.Collections.Immutable.ImmutableArray<string> hosts, CancellationToken ct) =>
            throw new NotImplementedException();

        public ValueTask CreateAsync(SchemataTenant tenant, CancellationToken ct) => throw new NotImplementedException();

        public ValueTask DeleteAsync(SchemataTenant tenant, CancellationToken ct) => throw new NotImplementedException();

        public ValueTask UpdateAsync(SchemataTenant tenant, CancellationToken ct) => throw new NotImplementedException();
    }
}
