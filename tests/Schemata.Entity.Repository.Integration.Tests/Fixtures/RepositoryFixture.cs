using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Event.Skeleton.Entities;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Push.Skeleton.Entities;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Entity.Repository.Integration.Tests.Fixtures;

public sealed class RepositoryFixture : IAsyncLifetime
{
    private SqliteConnection? _connection;
    private ServiceProvider?   _root;

    public IServiceProvider ServiceProvider => _root!;

    public async Task InitializeAsync() {
        _connection = new("Data Source=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();

        services.AddDbContextFactory<TestDbContext>(opts => opts.UseSqlite(_connection)
                                                       .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());

        services.AddRepository<SchemataProcess, EfCoreRepository<TestDbContext, SchemataProcess>>();
        services.AddRepository<SchemataProcessToken, EfCoreRepository<TestDbContext, SchemataProcessToken>>();
        services.AddRepository<SchemataProcessTransition, EfCoreRepository<TestDbContext, SchemataProcessTransition>>();
        services.AddRepository<SchemataJob, EfCoreRepository<TestDbContext, SchemataJob>>();
        services.AddRepository<SchemataJobExecution, EfCoreRepository<TestDbContext, SchemataJobExecution>>();
        services.AddRepository<SchemataPushSubscription, EfCoreRepository<TestDbContext, SchemataPushSubscription>>();
        services.AddRepository<SchemataEvent, EfCoreRepository<TestDbContext, SchemataEvent>>();

        services.AddScoped<IUnitOfWork<TestDbContext>, EfCoreUnitOfWork<TestDbContext>>();

        _root = services.BuildServiceProvider();

        await using var scope = _root.CreateAsyncScope();
        var       db    = scope.ServiceProvider.GetRequiredService<TestDbContext>();
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

    public (IRepository<TEntity> Repository, IServiceScope Scope) CreateScope<TEntity>()
        where TEntity : class {
        var scope      = _root!.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<TEntity>>();
        return (repository, scope);
    }
}