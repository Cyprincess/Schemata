using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Push.Foundation.Commands;
using Schemata.Push.Foundation.Handlers;
using Schemata.Push.Skeleton.Entities;

namespace Schemata.Push.Tests.Fixtures;

public sealed class EfCorePushSubscriptionFixture : IPushSubscriptionFixture
{
    private SqliteConnection? _connection;
    private ServiceProvider?  _root;

    public async Task InitializeAsync() {
        _connection = new("Data Source=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();

        services.AddDbContextFactory<PushDbContext>(options => options.UseSqlite(_connection)
                                                              .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());

        services.AddRepository<SchemataPushSubscription, EfCoreRepository<PushDbContext, SchemataPushSubscription>>();
        services.AddScoped<IUnitOfWork<PushDbContext>, EfCoreUnitOfWork<PushDbContext>>();
        services.AddSingleton(TimeProvider.System);
        services.TryAddScoped<AddPushSubscriptionHandler>();
        services.TryAddScoped<RemovePushSubscriptionHandler>();

        _root = services.BuildServiceProvider();

        await using var scope = _root.CreateAsyncScope();
        var           db    = scope.ServiceProvider.GetRequiredService<PushDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() {
        if (_root is not null) {
            await _root.DisposeAsync();
        }

        if (_connection is not null) {
            await _connection.DisposeAsync();
        }
    }

    public async Task<SchemataPushSubscription> AddAsync(AddPushSubscriptionRequest request) {
        await using var scope = _root!.CreateAsyncScope();
        var           handler   = scope.ServiceProvider.GetRequiredService<AddPushSubscriptionHandler>();
        return await handler.HandleAsync(request, CancellationToken.None);
    }

    public async Task RemoveAsync(RemovePushSubscriptionRequest request) {
        await using var scope = _root!.CreateAsyncScope();
        var           handler   = scope.ServiceProvider.GetRequiredService<RemovePushSubscriptionHandler>();
        await handler.HandleAsync(request, CancellationToken.None);
    }

    public async Task<List<SchemataPushSubscription>> SubscriptionsAsync() {
        await using var scope      = _root!.CreateAsyncScope();
        var           repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataPushSubscription>>();
        using (repository.SuppressQuerySoftDelete()) {
            var rows = new List<SchemataPushSubscription>();
            await foreach (var row in repository.ListAsync(query => query.OrderBy(row => row.Owner)
                                                                         .ThenBy(row => row.Provider)
                                                                         .ThenBy(row => row.ProviderKey),
                                                           CancellationToken.None)) {
                rows.Add(row);
            }

            return rows;
        }
    }
}
