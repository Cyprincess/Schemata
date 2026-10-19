using System;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Actor.Foundation.Features;
using Schemata.Actor.Skeleton;
using Schemata.Abstractions.Resource;
using Schemata.Core.Building;
using Schemata.Resource.Foundation;
using Schemata.Actor.Skeleton.Entities;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Push.Actor.Features;
using Schemata.Push.Skeleton.Entities;
using Schemata.Push.Skeleton.Control;

namespace Schemata.Push.Actor.Tests.Fixtures;

public sealed class PushActorConcurrencyHarness : IAsyncDisposable
{
    public required SqliteConnection Connection { get; init; }
    public required ServiceProvider  Root       { get; init; }

    public static async Task<PushActorConcurrencyHarness> BuildAsync(
        bool                                              withActor,
        IRepositoryAddAdvisor<SchemataPushSubscription>?  addAdvisor = null
    ) {
        var connectionString = $"Data Source=file:{Guid.NewGuid():n}?mode=memory&cache=shared";
        var connection       = new SqliteConnection(connectionString);
        connection.Open();

        var services = new ServiceCollection();
        services.AddDbContextFactory<TestDbContext>(options => options
                     .UseSqlite(connectionString)
                     .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<SchemataPushSubscription, EfCoreRepository<TestDbContext, SchemataPushSubscription>>();
        services.AddScoped<IUnitOfWork<TestDbContext>, EfCoreUnitOfWork<TestDbContext>>();
        var registry = new ResourceRegistry();
        registry.Add(new(typeof(SchemataPushSubscription)), []);
        registry.Add(new(typeof(TestOwner)), []);
        services.TryAddSingleton(registry);
        services.TryAddSingleton<IResourceTypeResolver, DefaultResourceTypeResolver>();
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataPushSubscription>, PushSubscriptionNameAdvisor>());
        if (addAdvisor is not null) {
            services.AddSingleton(addAdvisor);
        }

        services.AddLogging();
        services.AddAuthorization(options => {
            options.AddPolicy(PushPolicies.Create, policy => policy.RequireAuthenticatedUser().RequireClaim("permission", PushPolicies.Create));
            options.AddPolicy(PushPolicies.List, policy => policy.RequireAuthenticatedUser().RequireClaim("permission", PushPolicies.List));
            options.AddPolicy(PushPolicies.Delete, policy => policy.RequireAuthenticatedUser().RequireClaim("permission", PushPolicies.Delete));
        });
        services.AddSchemataPush();
        if (withActor) {
            new SchemataActorFeature<SchemataActor>().ConfigureServices(
                services,
                new(),
                new(),
                new ConfigurationBuilder().Build(),
                environment: null!);
            new SchemataPushActorFeature().ConfigureServices(
                services,
                new(),
                new(),
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
        Exception? failure = null;
        try {
            if (Root.GetService<IActorSystem>() is IActorSystemLifetime lifetime) {
                await lifetime.ShutdownAsync(default);
            }
        } catch (Exception error) { failure = error; }
        try { await Root.DisposeAsync(); }
        catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
        try { await Connection.DisposeAsync(); }
        catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}