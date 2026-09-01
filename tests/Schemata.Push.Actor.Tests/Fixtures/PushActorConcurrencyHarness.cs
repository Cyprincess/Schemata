using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Actor.Foundation.Features;
using Schemata.Core;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Push.Actor.Features;
using Schemata.Push.Skeleton.Entities;

namespace Schemata.Push.Actor.Tests.Fixtures;

public sealed class PushActorConcurrencyHarness : IAsyncDisposable
{
    public required SqliteConnection Connection { get; init; }
    public required ServiceProvider  Root       { get; init; }

    public static async Task<PushActorConcurrencyHarness> BuildAsync(bool withActor) {
        var connectionString = $"Data Source=file:{Guid.NewGuid():n}?mode=memory&cache=shared";
        var connection       = new SqliteConnection(connectionString);
        connection.Open();

        var services = new ServiceCollection();
        services.AddDbContextFactory<TestDbContext>(options => options
                     .UseSqlite(connectionString)
                     .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<SchemataPushSubscription, EfCoreRepository<TestDbContext, SchemataPushSubscription>>();
        services.AddScoped<IUnitOfWork<TestDbContext>, EfCoreUnitOfWork<TestDbContext>>();
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataPushSubscription>, PushSubscriptionNameAdvisor>());

        services.AddLogging();
        services.AddSchemataPush();
        if (withActor) {
            new SchemataActorFeature().ConfigureServices(
                services,
                new SchemataOptions(),
                new Configurators(),
                new ConfigurationBuilder().Build(),
                environment: null!);
            new SchemataPushActorFeature().ConfigureServices(
                services,
                new SchemataOptions(),
                new Configurators(),
                new ConfigurationBuilder().Build(),
                environment: null!);
        }

        var root = services.BuildServiceProvider();

        await using (var scope = root.CreateAsyncScope()) {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        return new() { Connection = connection, Root = root };
    }

    public async ValueTask DisposeAsync() {
        await Root.DisposeAsync();
        await Connection.DisposeAsync();
    }
}