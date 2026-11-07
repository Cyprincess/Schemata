using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Resource;
using Schemata.Core.Building;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Push.Foundation.Commands;
using Schemata.Push.Foundation.Handlers;
using Schemata.Push.Skeleton.Entities;
using Schemata.Resource.Foundation;

using Schemata.Abstractions.Advisors;
namespace Schemata.Push.Tests.Fixtures;

public sealed class EfCorePushSubscriptionFixture(
    bool nameAdvisor = true,
    bool blockCreate = false,
    bool blockUndelete = false) : IPushSubscriptionFixture
{
    private int _names;

    private readonly BlockPushSubscriptionUndelete _undeleteBlocker = new();

    private readonly string _dbPath = $"{Guid.NewGuid():n}.db";

    private ServiceProvider?  _root;

    public async Task InitializeAsync() {
        var services = new ServiceCollection();

        services.AddDbContextFactory<PushDbContext>(options => options.UseSqlite($"Data Source={_dbPath};Pooling=False")
                                                              .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());

        services.AddRepository<SchemataPushSubscription, EfCoreRepository<PushDbContext, SchemataPushSubscription>>();
        services.AddScoped<IUnitOfWork<PushDbContext>, EfCoreUnitOfWork<PushDbContext>>();
        services.AddSingleton(TimeProvider.System);
        PushTestResources.AddResourceTypeResolver(services, typeof(SchemataPushSubscription));

        if (nameAdvisor) {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IRepositoryAddAdvisor<SchemataPushSubscription>>(
                new AdviceAddSubscriptionName(() => $"subscription-{++_names}")));
        }
        if (blockCreate) {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IRepositoryAddAdvisor<SchemataPushSubscription>>(
                new BlockPushSubscriptionCreate()));
        }
        if (blockUndelete) {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IRepositoryUpdateAdvisor<SchemataPushSubscription>>(
                _undeleteBlocker));
        }

        services.TryAddScoped<AddPushSubscriptionHandler<SchemataPushSubscription>>();
        services.TryAddScoped<RemovePushSubscriptionHandler<SchemataPushSubscription>>();

        _root = services.BuildServiceProvider();

        await using var scope = _root.CreateAsyncScope();
        var           db    = scope.ServiceProvider.GetRequiredService<PushDbContext>();
        await db.Database.EnsureCreatedAsync();
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

    public async Task<SchemataPushSubscription?> AddAsync(AddPushSubscriptionRequest request) {
        await using var scope = _root!.CreateAsyncScope();
        var           handler   = scope.ServiceProvider.GetRequiredService<AddPushSubscriptionHandler<SchemataPushSubscription>>();
        return await handler.HandleAsync(request, new AdviceContext(_root!), CancellationToken.None);
    }

    public async Task RemoveAsync(RemovePushSubscriptionRequest request) {
        await using var scope = _root!.CreateAsyncScope();
        var           handler   = scope.ServiceProvider.GetRequiredService<RemovePushSubscriptionHandler<SchemataPushSubscription>>();
        await handler.HandleAsync(request, new AdviceContext(_root!), CancellationToken.None);
    }

    public void BlockUndelete() {
        _undeleteBlocker.Enabled = true;
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
}
