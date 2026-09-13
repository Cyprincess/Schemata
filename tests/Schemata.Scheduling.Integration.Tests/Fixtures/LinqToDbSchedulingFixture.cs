using System;
using TableAttribute = System.ComponentModel.DataAnnotations.Schema.TableAttribute;
using IndexAttribute = Schemata.Abstractions.Entities.IndexAttribute;
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
using Schemata.Scheduling.Foundation.Runtime;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Integration.Tests.Fixtures;

public sealed class SuccessJob : IScheduledJob
{
    public const string Key    = "jobs.success";
    public const string Output = """{"status":"done"}""";

    public Task ExecuteAsync(JobContext context, CancellationToken ct) {
        context.Execution!.Output = Output;

        return Task.CompletedTask;
    }
}

public sealed class FailingJob : IScheduledJob
{
    public const string Key = "jobs.failing";

    public Task ExecuteAsync(JobContext context, CancellationToken ct) {
        throw new InvalidOperationException("body exploded");
    }
}

public sealed class GatedJob : IScheduledJob
{
    public const string Key = "jobs.gated";

    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task ExecuteAsync(JobContext context, CancellationToken ct) {
        Entered.SetResult();
        await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
    }
}

public sealed class LinqToDbSchedulingFixture : IAsyncLifetime
{
    private readonly string _dbPath = $"{Guid.NewGuid():n}.db";

    private ServiceProvider? _root;

    public MutableClock Clock { get; } = new(new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    public IServiceProvider Services => _root!;

    public async Task InitializeAsync() {
        var schema = new MappingSchema();
        schema.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());

        var options = new DataOptions().UseSQLite($"Data Source={_dbPath}").UseMappingSchema(schema);

        var services = new ServiceCollection();

        services.TryAddScoped(_ => new SchedulingDataConnection(options));
        services.TryAddSingleton<Func<SchedulingDataConnection>>(_ => () => new(options));

        services.AddRepository<SchemataJob, LinqToDbRepository<SchedulingDataConnection, SchemataJob>>();
        services.AddRepository<SchemataJobExecution, LinqToDbRepository<SchedulingDataConnection, SchemataJobExecution>>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJob>, AdviceAddResourceName<SchemataJob>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJobExecution>, AdviceAddResourceName<SchemataJobExecution>>());
        services.AddScoped<IUnitOfWork<SchedulingDataConnection>, LinqToDbUnitOfWork<SchedulingDataConnection>>();

        var registry = new DefaultScheduledJobRegistry();
        registry.Register<SuccessJob>(SuccessJob.Key);
        registry.Register<FailingJob>(FailingJob.Key);
        registry.Register<GatedJob>(GatedJob.Key);

        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<IScheduledJobRegistry>(registry);
        services.AddSingleton<SuccessJob>();
        services.AddSingleton<FailingJob>();
        services.AddSingleton<GatedJob>();
        services.AddSchemataScheduling();

        _root = services.BuildServiceProvider();

        using var scope      = _root.CreateScope();
        var       connection = scope.ServiceProvider.GetRequiredService<SchedulingDataConnection>();
        CreateTable<SchemataJob>(connection);
        CreateTable<SchemataJobExecution>(connection);

        await _root.GetRequiredService<IScheduler>().StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() {
        if (_root is not null) {
            await _root.GetRequiredService<IScheduler>().StopAsync(CancellationToken.None);
            await _root.DisposeAsync();
        }

        SqliteConnection.ClearAllPools();

        if (File.Exists(_dbPath)) {
            File.Delete(_dbPath);
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

    public async Task<SchemataJobExecution?> ExecutionAsync(string jobKey) {
        var (repository, scope) = CreateScope<SchemataJobExecution>();
        using (scope) {
            return await repository.FirstOrDefaultAsync<SchemataJobExecution>(
                q => q.Where(execution => execution.JobKey == jobKey), CancellationToken.None);
        }
    }

    private static void CreateTable<TEntity>(SchedulingDataConnection connection)
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

public class SchedulingDataConnection(DataOptions options) : DataConnection(options);
