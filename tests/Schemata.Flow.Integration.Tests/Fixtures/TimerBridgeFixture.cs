using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Moq;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Core.Features;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Flow.Bpmn;
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
    private readonly string _connectionString = $"Data Source=flow-timer-{Guid.NewGuid():n};Mode=Memory;Cache=Shared";

    private SqliteConnection? _connection;
    private ServiceProvider?  _root;

    public ConcurrentQueue<TransitionRecord> Observed { get; } = new();

    #region IAsyncLifetime Members

    public async Task InitializeAsync() {
        var services = new ServiceCollection();
        Schemata.Flow.Tests.FlowTestCreation.Register(services);
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
        services.AddSchemataFlow();

        services.TryAddKeyedSingleton<IFlowRuntime<SchemataProcess, SchemataProcessToken, SchemataProcessTransition>, BpmnEngine>(FlowConstants.Engines.Bpmn);
        services.TryAddKeyedSingleton<IFlowRuntime>(FlowConstants.Engines.Bpmn,
            (sp, _) => sp.GetRequiredKeyedService<IFlowRuntime<SchemataProcess, SchemataProcessToken, SchemataProcessTransition>>(FlowConstants.Engines.Bpmn));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFlowEngineValidator, BpmnFlowEngineValidator>());

        ConfigureFeature(new SchemataSchedulingFeature<SchemataJob, SchemataJobExecution>(), services);
        services.AddSchemataSchedulingRepositoryStore();
        ConfigureFeature(new SchemataFlowSchedulingFeature<SchemataJob, SchemataJobExecution>(), services);
        // Claims no catch kind, so it never satisfies the has-an-owner check: it is here only to
        // record the transitions the timer path produces.
        var spy = new Mock<IFlowCatchHandler>();
        spy.Setup(handler => handler.Handles(It.IsAny<FlowCatchKind>())).Returns(false);
        spy.Setup(handler => handler.ArmAsync(It.IsAny<FlowTransitionContext>(), It.IsAny<CancellationToken>()))
           .Returns((FlowTransitionContext context, CancellationToken _) => {
               Observed.Enqueue(new(context.Snapshot.Process.CanonicalName!, context.Token.CanonicalName,
                                    context.PreviousWaitingAtName));
               return ValueTask.CompletedTask;
           });
        services.AddSingleton(spy.Object);
        // Claims message catches so gateway fixtures mixing message and timer branches satisfy the
        // has-an-owner check.
        var messages = new Mock<IFlowCatchHandler>();
        messages.Setup(handler => handler.Handles(It.IsAny<FlowCatchKind>()))
                .Returns((FlowCatchKind kind) => kind is FlowCatchKind.Message);
        messages.Setup(handler => handler.ArmAsync(It.IsAny<FlowTransitionContext>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);
        services.AddSingleton(messages.Object);

        _root = services.BuildServiceProvider();

        using (var scope = _root.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        var jobs = _root.GetRequiredService<IScheduledJobRegistry>();
        var options = _root.GetRequiredService<IOptions<SchemataSchedulingOptions>>();
        jobs.RegisterAll(options.Value.Jobs.Select(job => job.JobType));
        await _root.GetRequiredService<IScheduler<SchemataJob, SchemataJobExecution>>().StartAsync(default);

        var registry = _root.GetRequiredService<IProcessRegistry>();
        await registry.RegisterAsync<ParallelTimerProcess>(FlowConstants.Engines.Bpmn);
        await registry.RegisterAsync<SourceTimerProcess>(FlowConstants.Engines.Bpmn);
    }

    public async Task DisposeAsync() {
        if (_root is not null) {
            await _root.GetRequiredService<IScheduler<SchemataJob, SchemataJobExecution>>().StopAsync(default);

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
        return _root!.GetRequiredService<JobExecutionDispatcher<SchemataJob, SchemataJobExecution>>().DispatchPendingAsync(default);
    }

    private static void ConfigureFeature(FeatureBase feature, IServiceCollection services) {
        feature.ConfigureServices(services, new(), new(), new ConfigurationBuilder().Build(), null!);
    }
}
