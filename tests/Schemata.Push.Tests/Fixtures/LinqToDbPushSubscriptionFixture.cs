using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.LinqToDB;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Push.Foundation.Commands;
using Schemata.Push.Foundation.Handlers;
using Schemata.Push.Skeleton.Entities;
using IndexAttribute = Schemata.Abstractions.Entities.IndexAttribute;
using TableAttribute = System.ComponentModel.DataAnnotations.Schema.TableAttribute;

using Schemata.Abstractions.Advisors;
namespace Schemata.Push.Tests.Fixtures;

public sealed class LinqToDbPushSubscriptionFixture(bool nameAdvisor = true) : IPushSubscriptionFixture
{
    private int _names;

    private readonly string _dbPath = $"{Guid.NewGuid():n}.db";

    private ServiceProvider? _root;

    public async Task InitializeAsync() {
        var schema = new MappingSchema();
        schema.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());

        var options = new DataOptions().UseSQLite($"Data Source={_dbPath}").UseMappingSchema(schema);

        var services = new ServiceCollection();

        services.TryAddScoped(_ => new PushDataConnection(options));
        services.TryAddSingleton<Func<PushDataConnection>>(_ => () => new(options));

        services.AddRepository<SchemataPushSubscription, LinqToDbRepository<PushDataConnection, SchemataPushSubscription>>();
        services.AddScoped<IUnitOfWork<PushDataConnection>, LinqToDbUnitOfWork<PushDataConnection>>();
        services.AddSingleton(TimeProvider.System);

        if (nameAdvisor) {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IRepositoryAddAdvisor<SchemataPushSubscription>>(
                new AdviceAddSubscriptionName(() => $"subscription-{++_names}")));
        }
        services.TryAddScoped<AddPushSubscriptionHandler<SchemataPushSubscription>>();
        services.TryAddScoped<RemovePushSubscriptionHandler<SchemataPushSubscription>>();

        _root = services.BuildServiceProvider();

        await using var scope      = _root.CreateAsyncScope();
        var           connection = scope.ServiceProvider.GetRequiredService<PushDataConnection>();
        CreateTable<SchemataPushSubscription>(connection);
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

    public async Task<SchemataPushSubscription> AddAsync(AddPushSubscriptionRequest request) {
        await using var scope = _root!.CreateAsyncScope();
        var           handler   = scope.ServiceProvider.GetRequiredService<AddPushSubscriptionHandler<SchemataPushSubscription>>();
        return await handler.HandleAsync(request, new AdviceContext(_root!), CancellationToken.None);
    }

    public async Task RemoveAsync(RemovePushSubscriptionRequest request) {
        await using var scope = _root!.CreateAsyncScope();
        var           handler   = scope.ServiceProvider.GetRequiredService<RemovePushSubscriptionHandler<SchemataPushSubscription>>();
        await handler.HandleAsync(request, new AdviceContext(_root!), CancellationToken.None);
    }

    public async Task<List<SchemataPushSubscription>> SubscriptionsAsync() {
        await using var scope      = _root!.CreateAsyncScope();
        var           repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataPushSubscription>>();
        {
            var rows = new List<SchemataPushSubscription>();
            await foreach (var row in repository.ListAsync(query => query.OrderBy(row => row.Owner)
                                                                         .ThenBy(row => row.Provider)
                                                                         .ThenBy(row => row.ProviderKey),
                                                           CancellationToken.None,
                                                           new QueryOptions(IgnoreSoftDelete: true))) {
                rows.Add(row);
            }

            return rows;
        }
    }

    private static void CreateTable<TEntity>(DataConnection connection)
        where TEntity : class {
        connection.CreateTable<TEntity>(tableOptions: TableOptions.CreateIfNotExists);

        var table = typeof(TEntity).GetCustomAttribute<TableAttribute>()?.Name ?? typeof(TEntity).Name;
        foreach (var index in typeof(TEntity).GetCustomAttributes<IndexAttribute>()) {
            var name    = $"IX_{table}_{string.Join("_", index.Properties)}";
            var columns = string.Join(", ", index.Properties.Select(property => $"\"{property}\""));
            connection.Execute(
                $"CREATE {(index.IsUnique ? "UNIQUE " : string.Empty)}INDEX IF NOT EXISTS \"{name}\" ON \"{table}\" ({columns})");
        }
    }
}
