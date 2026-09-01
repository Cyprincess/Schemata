using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Owner;
using Schemata.Entity.Owner.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Flow.Bpmn;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.StateMachine;
using Xunit;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public sealed class OwnedSourceFixture : IAsyncLifetime
{
    private readonly string _connectionString = $"Data Source=flow-owned-{Guid.NewGuid():n};Mode=Memory;Cache=Shared";

    private SqliteConnection? _connection;
    private ServiceProvider?  _root;

    #region IAsyncLifetime Members

    public async Task InitializeAsync() {
        var services = new ServiceCollection();
        _connection = new(_connectionString);
        await _connection.OpenAsync();

        services.AddDbContextFactory<TestDbContext>(options => options.UseSqlite(_connectionString)
                                                               .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<OwnedOrder, EfCoreRepository<TestDbContext, OwnedOrder>>();
        services.AddRepository<SchemataProcess, EfCoreRepository<TestDbContext, SchemataProcess>>();
        services.AddRepository<SchemataProcessToken, EfCoreRepository<TestDbContext, SchemataProcessToken>>();
        services.AddRepository<SchemataProcessTransition, EfCoreRepository<TestDbContext, SchemataProcessTransition>>();
        services.AddRepository<SchemataProcessSource, EfCoreRepository<TestDbContext, SchemataProcessSource>>();
        services.AddRepository<SchemataProcessCompensation, EfCoreRepository<TestDbContext, SchemataProcessCompensation>>();
        services.AddScoped<IUnitOfWork<TestDbContext>, EfCoreUnitOfWork<TestDbContext>>();
        FlowFixtureServices.AddResourceTypeResolver(
            services, typeof(OwnedOrder), typeof(SchemataProcess), typeof(SchemataProcessToken));

        services.AddLogging();

        services.AddOptions<SchemataOwnerOptions>();
        services.AddSingleton(typeof(IOwnerResolver<>), typeof(AmbientOwnerResolver<>));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryBuildQueryAdvisor<>), typeof(AdviceBuildQueryOwner<>)));

        services.AddOptions<SchemataFlowOptions>();

        var timers = new Mock<IFlowCatchHandler>();
        timers.Setup(handler => handler.Handles(It.IsAny<FlowCatchKind>()))
              .Returns<FlowCatchKind>(kind => kind is FlowCatchKind.Timer);
        services.AddSingleton(timers.Object);

        services.AddSchemataFlow();

        services.TryAddKeyedSingleton<IFlowRuntime, StateMachineEngine>(FlowConstants.Engines.StateMachine);
        services.TryAddKeyedSingleton<IFlowRuntime, BpmnEngine>(FlowConstants.Engines.Bpmn);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFlowEngineValidator, StateMachineFlowEngineValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFlowEngineValidator, BpmnFlowEngineValidator>());

        _root = services.BuildServiceProvider();

        using (var scope = _root.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        var registry = _root.GetRequiredService<IProcessRegistry>();
        await registry.RegisterAsync<OwnedTimerProcess>(FlowConstants.Engines.Bpmn);
        await registry.RegisterAsync<OwnedTaskProcess>(FlowConstants.Engines.StateMachine);
    }

    public async Task DisposeAsync() {
        if (_root is not null) {
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
}
