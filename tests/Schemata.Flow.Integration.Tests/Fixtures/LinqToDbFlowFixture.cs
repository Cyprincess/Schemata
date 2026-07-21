using System;
using System.IO;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Common;
using Schemata.Entity.LinqToDB;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;
using Xunit;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public sealed class LinqToDbFlowFixture : IAsyncLifetime, IFlowIntegrationFixture
{
    private readonly string _dbPath = $"{Identifiers.NewUid():n}.db";

    private ServiceProvider? _root;

    #region IAsyncLifetime Members

    public async Task InitializeAsync() {
        var schema = new MappingSchema();
        schema.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());

        var services = new ServiceCollection();
        var options  = new DataOptions().UseSQLite($"Data Source={_dbPath}").UseMappingSchema(schema);
        services.TryAddScoped(_ => new TestDataConnection(options));
        services.TryAddSingleton<Func<TestDataConnection>>(_ => () => new(options));
        services.AddRepository<Order, LinqToDbRepository<TestDataConnection, Order>>();
        services.AddRepository<SchemataProcess, LinqToDbRepository<TestDataConnection, SchemataProcess>>();
        services.AddRepository<SchemataProcessToken, LinqToDbRepository<TestDataConnection, SchemataProcessToken>>();
        services.AddRepository<SchemataProcessTransition, LinqToDbRepository<TestDataConnection, SchemataProcessTransition>>();
        services.AddRepository<SchemataProcessSource, LinqToDbRepository<TestDataConnection, SchemataProcessSource>>();
        services.AddRepository<SchemataProcessCompensation, LinqToDbRepository<TestDataConnection, SchemataProcessCompensation>>();
        services.AddScoped<IUnitOfWork<TestDataConnection>, LinqToDbUnitOfWork<TestDataConnection>>();
        FlowFixtureServices.AddResourceTypeResolver(
            services, typeof(Order), typeof(SchemataProcess), typeof(SchemataProcessToken));
        FlowFixtureServices.AddFlowServices(services);

        _root = services.BuildServiceProvider();

        using (var scope = _root.CreateScope()) {
            var connection = scope.ServiceProvider.GetRequiredService<TestDataConnection>();
            connection.CreateTable<Order>(tableOptions: TableOptions.CreateIfNotExists);
            connection.CreateTable<SchemataProcess>(tableOptions: TableOptions.CreateIfNotExists);
            connection.Execute("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_SchemataProcesses_DefinitionName_IdempotencyKey\" ON \"SchemataProcesses\" (\"DefinitionName\", \"IdempotencyKey\")");
            connection.CreateTable<SchemataProcessToken>(tableOptions: TableOptions.CreateIfNotExists);
            connection.CreateTable<SchemataProcessTransition>(tableOptions: TableOptions.CreateIfNotExists);
            connection.CreateTable<SchemataProcessSource>(tableOptions: TableOptions.CreateIfNotExists);
            connection.CreateTable<SchemataProcessCompensation>(tableOptions: TableOptions.CreateIfNotExists);
        }

        await FlowFixtureServices.RegisterProcessesAsync(_root);
    }

    public Task DisposeAsync() {
        _root?.Dispose();
        SqliteConnection.ClearAllPools();

        if (File.Exists(_dbPath)) {
            File.Delete(_dbPath);
        }

        return Task.CompletedTask;
    }

    #endregion

    public IServiceScope CreateScope() { return _root!.CreateScope(); }
}
