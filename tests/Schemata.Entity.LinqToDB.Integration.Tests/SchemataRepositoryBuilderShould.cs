using System;
using LinqToDB;
using LinqToDB.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Entity.LinqToDB.Integration.Tests;

[Trait("Category", "Integration")]
public class SchemataRepositoryBuilderShould
{
    [Fact]
    public void ConnectionFactory_ResolvesDependencies_FromTheCallingScope() {
        var services = new ServiceCollection();
        services.AddScoped<ScopeMarker>();

        var builder = new SchemataRepositoryBuilder(services);
        builder.UseLinqToDb<ScopeBoundConnection>((_, options) => options.UseSQLite("Data Source=:memory:"));
        builder.WithUnitOfWork<ScopeBoundConnection>();

        using var root   = services.BuildServiceProvider();
        using var first  = root.CreateScope();
        using var second = root.CreateScope();

        var firstConnection  = first.ServiceProvider.GetRequiredService<Func<ScopeBoundConnection>>()();
        var firstAgain       = first.ServiceProvider.GetRequiredService<Func<ScopeBoundConnection>>()();
        var secondConnection = second.ServiceProvider.GetRequiredService<Func<ScopeBoundConnection>>()();

        Assert.Same(firstConnection.Marker, firstAgain.Marker);
        Assert.NotSame(firstConnection.Marker, secondConnection.Marker);

        using var uow = first.ServiceProvider.GetRequiredService<IUnitOfWork<ScopeBoundConnection>>();
        Assert.Same(firstConnection.Marker, uow.Context.Marker);
    }

    [Fact]
    public void ConnectionFactory_CreatesFreshConnection_PerInvocation() {
        var services = new ServiceCollection();
        services.AddScoped<ScopeMarker>();

        var builder = new SchemataRepositoryBuilder(services);
        builder.UseLinqToDb<ScopeBoundConnection>((_, options) => options.UseSQLite("Data Source=:memory:"));

        using var root  = services.BuildServiceProvider();
        using var scope = root.CreateScope();

        var factory        = scope.ServiceProvider.GetRequiredService<Func<ScopeBoundConnection>>();
        using var first  = factory();
        using var second = factory();

        Assert.NotSame(first, second);
    }

    #region Nested type: ScopeMarker

    private sealed class ScopeMarker { }

    #endregion

    #region Nested type: ScopeBoundConnection

    private sealed class ScopeBoundConnection : DataConnection
    {
        public ScopeBoundConnection(DataOptions options, ScopeMarker marker) : base(options) {
            Marker = marker;
        }

        public ScopeMarker Marker { get; }
    }

    #endregion
}
