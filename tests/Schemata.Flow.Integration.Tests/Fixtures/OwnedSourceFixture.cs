using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions;
using Schemata.Abstractions.Entities;
using Schemata.Common;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Owner;
using Schemata.Entity.Owner.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Flow.Bpmn;
using Schemata.Flow.Foundation;
using Schemata.Flow.Foundation.Advisors;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.StateMachine;
using Xunit;

namespace Schemata.Flow.Integration.Tests.Fixtures;

[Table("OwnedOrders")]
[CanonicalName("ownedOrders/{ownedOrder}")]
[Microsoft.EntityFrameworkCore.PrimaryKey(nameof(Uid))]
public sealed class OwnedOrder : IIdentifier, ICanonicalName, IStateful, IOwnable, ISoftDelete
{
    public string? TaskValue { get; set; }

    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IIdentifier Members

    public Guid Uid { get; set; }

    #endregion

    #region IOwnable Members

    public string? Owner { get; set; }

    #endregion

    #region ISoftDelete Members

    public DateTime? DeleteTime { get; set; }
    public DateTime? PurgeTime  { get; set; }

    #endregion

    #region IStateful Members

    public string? State { get; set; }

    #endregion
}

public static class AmbientOwner
{
    public static readonly AsyncLocal<string?> Current = new();
}

public sealed class AmbientOwnerResolver<TEntity> : IOwnerResolver<TEntity>
{
    public ValueTask<string?> ResolveAsync(CancellationToken ct) {
        return ValueTask.FromResult(AmbientOwner.Current.Value);
    }
}

public sealed class OwnedTimerProcess : ProcessDefinition
{
    public OwnedTimerProcess() {
        BindSource<OwnedOrder>(order => order.State);

        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var timer = new FlowEvent {
            Name     = "wait",
            Position = EventPosition.IntermediateCatch,
            Definition = new TimerDefinition {
                Name           = "owned-timer",
                TimerType      = TimerType.Duration,
                TimeExpression = XmlConvert.ToString(TimeSpan.FromHours(1)),
            },
        };
        var apply = new NoneTask { Name = "apply" };
        var end   = new FlowEvent { Name = "end", Position = EventPosition.End };

        Elements.Add(start);
        Elements.Add(timer);
        Elements.Add(apply);
        Elements.Add(end);

        Flows.Add(new() { Source = start, Target = timer });
        Flows.Add(new() { Source = timer, Target = apply });
        Flows.Add(new() { Source = apply, Target = end });
    }
}

public sealed class OwnedTaskProcess : ProcessDefinition
{
    public OwnedTaskProcess() {
        BindSource<OwnedOrder>(projection: FlowSourceProjection.None);
        this.Start().Go(Review);
        this.During(Review).Go(Apply);
        this.During(Apply).OnEnter<OwnedOrder>(Mutate).End();
    }

    public UserTask Review { get; } = null!;
    public UserTask Apply  { get; } = null!;

    private static ValueTask Mutate(FlowTaskContext _, OwnedOrder order) {
        order.TaskValue = "touched";
        return ValueTask.CompletedTask;
    }
}

public sealed class OwnedSourceFixture : IAsyncLifetime
{
    private readonly string _connectionString = $"Data Source=flow-owned-{Identifiers.NewUid():n};Mode=Memory;Cache=Shared";

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
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryAddAdvisor<>), typeof(AdviceAddIdentifier<>)));

        services.AddOptions<SchemataOwnerOptions>();
        services.AddSingleton(typeof(IOwnerResolver<>), typeof(AmbientOwnerResolver<>));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryBuildQueryAdvisor<>), typeof(AdviceBuildQueryOwner<>)));

        services.AddOptions<SchemataFlowOptions>();
        services.Configure<SchemataFlowOptions>(options => options.Bridges.Add(SchemataFlowOptions.TimersBridge));

        services.TryAddSingleton<IProcessRegistry, ProcessRegistry>();
        services.TryAddSingleton<ProcessPersistence>();
        services.TryAddScoped<ProcessLifecycleNotifier>();
        services.TryAddScoped<FlowRunner>();
        services.TryAddScoped<IFlowRunner>(sp => sp.GetRequiredService<FlowRunner>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IFlowSourceAdvisor<>), typeof(AdviceSourceProjection<>)));

        services.TryAddKeyedSingleton<IFlowRuntime, StateMachineEngine>(SchemataConstants.FlowEngines.StateMachine);
        services.TryAddKeyedSingleton<IFlowRuntime, BpmnEngine>(SchemataConstants.FlowEngines.Bpmn);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFlowEngineValidator, StateMachineFlowEngineValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFlowEngineValidator, BpmnFlowEngineValidator>());

        _root = services.BuildServiceProvider();

        using (var scope = _root.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        var registry = _root.GetRequiredService<IProcessRegistry>();
        await registry.RegisterAsync<OwnedTimerProcess>(SchemataConstants.FlowEngines.Bpmn);
        await registry.RegisterAsync<OwnedTaskProcess>(SchemataConstants.FlowEngines.StateMachine);
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
