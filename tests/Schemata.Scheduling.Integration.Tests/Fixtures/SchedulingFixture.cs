using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Scheduling.Foundation.Runtime;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Integration.Tests.Fixtures;

public sealed class SchedulingFixture : IAsyncLifetime
{
    private SqliteConnection? _connection;
    private ServiceProvider?   _root;

    public MutableClock Clock { get; } = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    public BlockingJob BlockingJob { get; } = new();

    public BlockingJobUpdateAdvisor BlockingJobUpdateAdvisor { get; } = new();

    public IServiceProvider Services => _root!;

    public async Task InitializeAsync() {
        _connection = new("Data Source=:memory:");
        await _connection.OpenAsync();

        var registry = new DefaultScheduledJobRegistry();
        registry.Register<BlockingJob>(BlockingJob.Key);

        var services = new ServiceCollection();

        services.AddDbContextFactory<SchedulingDbContext>(opts => opts.UseSqlite(_connection)
                                                       .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());

        services.AddRepository<SchemataJob, EfCoreRepository<SchedulingDbContext, SchemataJob>>();
        services.AddRepository<SchemataJobExecution, EfCoreRepository<SchedulingDbContext, SchemataJobExecution>>();

        services.AddScoped<IUnitOfWork<SchedulingDbContext>, EfCoreUnitOfWork<SchedulingDbContext>>();

        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<IScheduledJobRegistry>(registry);
        services.AddSingleton(BlockingJob);
        services.AddSingleton<IRepositoryUpdateAdvisor<SchemataJob>>(BlockingJobUpdateAdvisor);
        services.AddSchemataScheduling();

        _root = services.BuildServiceProvider();

        await using (var scope = _root.CreateAsyncScope()) {
            var db = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        await _root.GetRequiredService<IScheduler>().StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() {
        if (_root is not null) {
            await _root.GetRequiredService<IScheduler>().StopAsync(CancellationToken.None);
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

    public async Task<SchemataJob?> JobAsync(string name) {
        var (repository, scope) = CreateScope<SchemataJob>();
        using (scope) {
            return await repository.FirstOrDefaultAsync<SchemataJob>(
                q => q.Where(job => job.Name == name), CancellationToken.None);
        }
    }
}
