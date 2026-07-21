using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Common;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Flow.Bpmn;
using Schemata.Flow.Foundation;
using Schemata.Flow.Foundation.Advisors;
using Schemata.Flow.Scheduling.Features;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Foundation.Features;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public sealed class TimerBridgeFixture : IAsyncLifetime
{
    private readonly string _connectionString = $"Data Source=flow-timer-{Identifiers.NewUid():n};Mode=Memory;Cache=Shared";

    private SqliteConnection? _connection;
    private ServiceProvider?  _root;

    public RecordingTransitionAdvisor Spy { get; } = new();

    #region IAsyncLifetime Members

    public async Task InitializeAsync() {
        var services = new ServiceCollection();
        _connection = new(_connectionString);
        await _connection.OpenAsync();

        services.AddDbContextFactory<TestDbContext>(options => options.UseSqlite(_connectionString)
                                                               .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<Order, EfCoreRepository<TestDbContext, Order>>();
        services.AddRepository<SchemataProcess, EfCoreRepository<TestDbContext, SchemataProcess>>();
        services.AddRepository<SchemataProcessToken, EfCoreRepository<TestDbContext, SchemataProcessToken>>();
        services.AddRepository<SchemataProcessTransition, EfCoreRepository<TestDbContext, SchemataProcessTransition>>();
        services.AddRepository<SchemataProcessSource, EfCoreRepository<TestDbContext, SchemataProcessSource>>();
        services.AddRepository<SchemataProcessCompensation, EfCoreRepository<TestDbContext, SchemataProcessCompensation>>();
        services.AddRepository<SchemataJob, EfCoreRepository<TestDbContext, SchemataJob>>();
        services.AddRepository<SchemataJobExecution, EfCoreRepository<TestDbContext, SchemataJobExecution>>();
        services.AddScoped<IUnitOfWork<TestDbContext>, EfCoreUnitOfWork<TestDbContext>>();
        FlowFixtureServices.AddResourceTypeResolver(
            services, typeof(Order), typeof(SchemataProcess), typeof(SchemataProcessToken), typeof(SchemataJob),
            typeof(SchemataJobExecution));

        services.AddLogging();
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryAddAdvisor<>), typeof(AdviceAddIdentifier<>)));
        services.TryAddSingleton<IProcessRegistry, ProcessRegistry>();
        services.TryAddSingleton<ProcessPersistence>();
        services.TryAddScoped<ProcessLifecycleNotifier>();
        services.TryAddScoped<FlowRunner>();
        services.TryAddScoped<IFlowRunner>(sp => sp.GetRequiredService<FlowRunner>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IFlowSourceAdvisor<>), typeof(AdviceSourceProjection<>)));

        services.TryAddKeyedSingleton<IFlowRuntime, BpmnEngine>(SchemataConstants.FlowEngines.Bpmn);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFlowEngineValidator, BpmnFlowEngineValidator>());

        ConfigureFeature(new SchemataSchedulingFeature(), services);
        ConfigureFeature(new SchemataFlowSchedulingFeature(), services);
        services.AddSingleton<IFlowTransitionAdvisor>(Spy);

        _root = services.BuildServiceProvider();

        using (var scope = _root.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        var jobs = _root.GetRequiredService<IScheduledJobRegistry>();
        var options = _root.GetRequiredService<IOptions<SchemataSchedulingOptions>>();
        jobs.RegisterAll(options.Value.Jobs.Select(job => job.JobType));
        await _root.GetRequiredService<IScheduler>().StartAsync(default);

        var registry = _root.GetRequiredService<IProcessRegistry>();
        await registry.RegisterAsync<ParallelTimerProcess>(SchemataConstants.FlowEngines.Bpmn);
        await registry.RegisterAsync<SourceTimerProcess>(SchemataConstants.FlowEngines.Bpmn);
    }

    public async Task DisposeAsync() {
        if (_root is not null) {
            await _root.GetRequiredService<IScheduler>().StopAsync(default);

            using (var scope = _root.CreateScope()) {
                var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
                await db.Database.EnsureDeletedAsync();
            }

            await _root.DisposeAsync();
        }

        if (_connection is not null) {
            await _connection.DisposeAsync();
        }
    }

    #endregion

    public IServiceScope CreateScope() { return _root!.CreateScope(); }

    public Task DispatchPendingAsync() {
        return _root!.GetRequiredService<JobExecutionDispatcher>().DispatchPendingAsync(default);
    }

    private static void ConfigureFeature(FeatureBase feature, IServiceCollection services) {
        feature.ConfigureServices(services, new(), new(), new ConfigurationBuilder().Build(), null!);
    }
}
