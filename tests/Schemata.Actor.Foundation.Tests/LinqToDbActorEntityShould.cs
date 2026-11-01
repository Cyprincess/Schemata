using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Actor.Foundation.Tests.Fixtures;
using Schemata.Actor.Skeleton;
using Schemata.Core;
using Schemata.Entity.LinqToDB;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Xunit;

namespace Schemata.Actor.Foundation.Tests;

/// <summary>
///     Persists the host-defined <see cref="SessionActor" /> state row through a real
///     <see cref="LinqToDbRepository{TContext,TEntity}" />, proving the entity closes over the
///     provider's model conventions and not only EF Core.
/// </summary>
[Trait("Category", "Integration")]
public sealed class LinqToDbActorEntityShould : IAsyncLifetime
{
    private readonly string _dbPath = $"{Guid.NewGuid():n}.db";

    private ServiceProvider? _root;

    #region IAsyncLifetime Members

    public Task InitializeAsync() {
        var schema = new MappingSchema();
        schema.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());
        var options = new DataOptions().UseSQLite($"Data Source={_dbPath}").UseMappingSchema(schema);

        var services = new ServiceCollection();
        services.TryAddScoped(_ => new SessionConnection(options));
        services.TryAddSingleton<Func<SessionConnection>>(_ => () => new(options));
        services.AddRepository<SessionActor, LinqToDbRepository<SessionConnection, SessionActor>>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SessionActor>, SessionActorNameAdvisor>());

        var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
        builder.UseActor<SessionActor>(actor => {
            actor.Register<CounterPersistentActor>("counter");
            actor.UsePersistence();
        });
        builder.Invoke(services);

        _root = services.BuildServiceProvider();

        using var scope      = _root.CreateScope();
        var       connection = scope.ServiceProvider.GetRequiredService<SessionConnection>();
        connection.CreateTable<SessionActor>(tableOptions: TableOptions.CreateIfNotExists);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync() {
        if (_root is not null) {
            await _root.DisposeAsync();
        }

        SqliteConnection.ClearAllPools();

        if (File.Exists(_dbPath)) {
            File.Delete(_dbPath);
        }
    }

    #endregion

    [Fact]
    public async Task Persist_State_Through_A_LinqToDb_Repository() {
        var system = _root!.GetRequiredService<IActorSystem>();
        var id     = new ActorId("counter", "linqto-db");
        var actor  = await system.GetAsync(id);
        await actor.AskAsync<Increment, int>(new());
        await system.StopAsync(id);

        var respawned = await system.GetAsync(id);
        Assert.Equal(1, await respawned.AskAsync<GetCount, int>(new()));

        await using var verify = _root!.CreateAsyncScope();
        var repository = verify.ServiceProvider.GetRequiredService<IRepository<SessionActor>>();
        var row = await repository.FirstOrDefaultAsync<SessionActor>(
            q => q.Where(state => state.ActorType == id.Type && state.ActorKey == id.Key), CancellationToken.None);

        Assert.NotNull(row);
        Assert.StartsWith("session-", row.Name, StringComparison.Ordinal);
        Assert.Equal(1, BitConverter.ToInt32(row.State!, 0));
        await ((IActorSystemLifetime)system).ShutdownAsync(CancellationToken.None);
    }

    private sealed class SessionConnection(DataOptions options) : DataConnection(options);
}
