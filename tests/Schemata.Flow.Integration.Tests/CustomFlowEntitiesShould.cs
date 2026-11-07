using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Entity.LinqToDB;
using Schemata.Entity.Repository;
using Schemata.Flow.Bpmn.Extensions;
using Schemata.Flow.Foundation;
using Schemata.Flow.Integration.Tests.Fixtures;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Flow.Skeleton.Entities;
using Xunit;
using TenantFlowRunner = Schemata.Flow.Foundation.IFlowRunner<
    Schemata.Flow.Integration.Tests.Fixtures.TenantProcess,
    Schemata.Flow.Integration.Tests.Fixtures.TenantProcessToken>;

namespace Schemata.Flow.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Integration")]
public sealed class CustomFlowEntitiesShould
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Persist_Host_Defined_Flow_Entities_Through_Start_Signal_And_Complete(bool linq) {
        var dbPath = $"{Guid.NewGuid():n}.db";
        var services = new ServiceCollection();
        if (linq) {
            var schema = new MappingSchema();
            schema.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());
            FlowFixtureServices.AddTenantFlowRepositories(
                services, new DataOptions().UseSQLite($"Data Source={dbPath}").UseMappingSchema(schema));
        } else {
            FlowFixtureServices.AddTenantFlowRepositories(services, dbPath);
        }

        FlowFixtureServices.AddResourceTypeResolver(services, typeof(TenantProcess), typeof(TenantProcessToken),
            typeof(TenantProcessTransition), typeof(TenantProcessSource));
        Schemata.Flow.Tests.FlowTestCreation.Register(services);
        services.AddLogging();

        var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
        builder.UseFlow<TenantProcess, TenantProcessToken, TenantProcessTransition, TenantProcessCompensation,
                   TenantProcessParticipant, TenantProcessSource, TenantFlowEffectIntent>()
               .UseBpmn<TenantProcess, TenantProcessToken, TenantProcessTransition, TenantProcessCompensation,
                   TenantProcessParticipant, TenantProcessSource, TenantFlowEffectIntent>()
               .Use<TenantSignalProcess>(FlowConstants.Engines.Bpmn);
        builder.Invoke(services);
        services.AddSingleton<IFlowCatchHandler, SignalCatchHandler>();

        await using var root = services.BuildServiceProvider();
        try {
            if (linq) {
                using var scope = root.CreateScope();
                var connection = scope.ServiceProvider.GetRequiredService<TestDataConnection>();
                connection.CreateTable<TenantProcess>(tableOptions: TableOptions.CreateIfNotExists);
                connection.CreateTable<TenantProcessToken>(tableOptions: TableOptions.CreateIfNotExists);
                connection.CreateTable<TenantProcessTransition>(tableOptions: TableOptions.CreateIfNotExists);
                connection.CreateTable<TenantProcessCompensation>(tableOptions: TableOptions.CreateIfNotExists);
                connection.CreateTable<TenantProcessParticipant>(tableOptions: TableOptions.CreateIfNotExists);
                connection.CreateTable<TenantProcessSource>(tableOptions: TableOptions.CreateIfNotExists);
                connection.CreateTable<TenantFlowEffectIntent>(tableOptions: TableOptions.CreateIfNotExists);
            } else {
                await using var scope = root.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<TenantFlowDbContext>().Database.EnsureCreatedAsync();
            }

            TenantProcess process;
            await using (var scope = root.CreateAsyncScope()) {
                var runner = scope.ServiceProvider.GetRequiredService<TenantFlowRunner>();
                process = (TenantProcess)await runner.StartAsync(nameof(TenantSignalProcess), null, CancellationToken.None);
            }

            Assert.Equal(nameof(TenantSignalProcess), process.DefinitionName);
            var processName = process.CanonicalName!;
            var processCanonical = process.CanonicalName!;

            await using (var scope = root.CreateAsyncScope()) {
                var token = await SingleTokenAsync(scope.ServiceProvider, processName);
                Assert.Equal("signal-catch", token.WaitingAtName);
                Assert.Equal("Waiting", token.State);
                token.Bookkeeping["origin"] = 42;
                var tokens = scope.ServiceProvider.GetRequiredService<IRepository<TenantProcessToken>>();
                await tokens.UpdateAsync(token);
                await tokens.CommitAsync();
            }

            await using (var scope = root.CreateAsyncScope()) {
                var runner = scope.ServiceProvider.GetRequiredService<TenantFlowRunner>();
                var results = await runner.ThrowSignalAsync(TenantSignalProcess.EventSignalName, (string?)null, null, null, CancellationToken.None);
                var delivery = Assert.Single(results);
                Assert.Equal(SignalDeliveryStatus.Delivered, delivery.Status);
                Assert.Equal(processCanonical, delivery.ProcessCanonicalName);
            }

            await using (var scope = root.CreateAsyncScope()) {
                var repository = scope.ServiceProvider.GetRequiredService<IRepository<TenantProcessToken>>();
                var tokens = new List<TenantProcessToken>();
                await foreach (var token in repository.ListAsync<TenantProcessToken>(query => query.Where(t => t.Process == processName))) {
                    tokens.Add(token);
                }

                Assert.Equal(2, tokens.Count);
                var parent = Assert.Single(tokens, token => token.ScopeName == "");
                var child = Assert.IsType<TenantProcessToken>(Assert.Single(tokens, token => token.ScopeName == "event-sub"));
                Assert.Equal(parent.CanonicalName, child.Spawner);
                Assert.Equal("event-task", child.StateName);
                Assert.Null(child.WaitingAtName);
                Assert.Equal("Active", child.State);
                Assert.Equal(42, child.Bookkeeping["origin"]);

                var processes = scope.ServiceProvider.GetRequiredService<IRepository<TenantProcess>>();
                var current = await processes.FirstOrDefaultAsync(q => q.Where(p => p.CanonicalName == processCanonical));
                await scope.ServiceProvider.GetRequiredService<TenantFlowRunner>()
                           .CompleteAsync(current!, child.CanonicalName, null, CancellationToken.None);
            }

            await using (var scope = root.CreateAsyncScope()) {
                var runner = scope.ServiceProvider.GetRequiredService<TenantFlowRunner>();
                var results = await runner.ThrowSignalAsync(TenantSignalProcess.SignalName, (string?)null, null, null, CancellationToken.None);
                var delivery = Assert.Single(results);
                Assert.Equal(SignalDeliveryStatus.Delivered, delivery.Status);
                Assert.Equal(processCanonical, delivery.ProcessCanonicalName);
            }

            await using (var scope = root.CreateAsyncScope()) {
                var token = await SingleTokenAsync(scope.ServiceProvider, processName);
                Assert.Equal("work", token.StateName);
                Assert.Null(token.WaitingAtName);
                Assert.Equal("Active", token.State);

                var processes = scope.ServiceProvider.GetRequiredService<IRepository<TenantProcess>>();
                var current = await processes.FirstOrDefaultAsync(q => q.Where(p => p.CanonicalName == processCanonical));
                Assert.IsType<TenantProcess>(current);
                await scope.ServiceProvider.GetRequiredService<TenantFlowRunner>()
                           .CompleteAsync(current!, token.CanonicalName, null, CancellationToken.None);
            }

            await using (var scope = root.CreateAsyncScope()) {
                var processes = scope.ServiceProvider.GetRequiredService<IRepository<TenantProcess>>();
                var settled = Assert.IsType<TenantProcess>(await processes.FirstOrDefaultAsync(q => q.Where(p => p.CanonicalName == processCanonical)));
                Assert.Equal("Completed", settled.State);

                var token = await SingleTokenAsync(scope.ServiceProvider, processName);
                Assert.Equal("Completed", token.State);
                Assert.Null(token.WaitingAtName);

                var transitions = new List<TenantProcessTransition>();
                await foreach (var transition in scope.ServiceProvider.GetRequiredService<IRepository<TenantProcessTransition>>()
                                                          .ListAsync<TenantProcessTransition>(null)) {
                    transitions.Add(transition);
                }
                var rootTransitions = transitions.Where(transition => transition.Token == token.CanonicalName).ToList();
                Assert.NotEmpty(rootTransitions);
                Assert.All(rootTransitions, transition => Assert.IsType<TenantProcessTransition>(transition));
                Assert.Contains(rootTransitions, transition => transition.Posterior == "signal-catch");
                Assert.Contains(rootTransitions, transition => transition.Posterior == "work");

                Assert.Null(root.GetService<IRepository<SchemataProcess>>());
                Assert.Null(root.GetService<IRepository<SchemataProcessToken>>());
            }
        } finally {
            await root.DisposeAsync();
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            SqliteConnection.ClearPool(connection);
            if (File.Exists(dbPath)) {
                File.Delete(dbPath);
            }
        }
    }

    private static async Task<TenantProcessToken> SingleTokenAsync(IServiceProvider services, string processName) {
        var tokens = services.GetRequiredService<IRepository<TenantProcessToken>>();
        var rows = new List<TenantProcessToken>();
        await foreach (var token in tokens.ListAsync<TenantProcessToken>(query => query.Where(t => t.Process == processName && t.ScopeName == ""))) {
            rows.Add(token);
        }
        return Assert.IsType<TenantProcessToken>(Assert.Single(rows));
    }

    private sealed class SignalCatchHandler : IFlowCatchHandler
    {
        public bool Handles(FlowCatchKind kind) { return kind == FlowCatchKind.Signal; }

        public ValueTask ArmAsync(FlowTransitionContext context, CancellationToken ct = default) { return ValueTask.CompletedTask; }
    }
}

public sealed class TenantSignalProcess : ProcessDefinition
{
    public const string SignalName = "tenant-signal";
    public const string EventSignalName = "tenant-event-signal";

    public TenantSignalProcess() {
        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var signal = new Signal { Name = SignalName };
        var eventSignal = new Signal { Name = EventSignalName };
        var modelSignal = new Signal { Name = "tenant-model-signal" };
        var catchEvent = new FlowEvent {
            Name       = "signal-catch",
            Position   = EventPosition.IntermediateCatch,
            Definition = signal,
        };
        var work = new NoneTask { Name = "work" };
        var end  = new FlowEvent { Name = "end", Position = EventPosition.End };
        var modelBoundary = new FlowEvent {
            Name = "model-boundary", Position = EventPosition.Boundary, AttachedTo = work,
            Definition = modelSignal, Interrupting = false,
        };
        var eventSub = new EventSubProcess { Name = "event-sub" };
        var eventStart = new FlowEvent {
            Name = "event-start", Position = EventPosition.Start, Definition = eventSignal, Interrupting = false,
        };
        var eventTask = new NoneTask { Name = "event-task" };
        var eventEnd = new FlowEvent { Name = "event-end", Position = EventPosition.End };
        eventSub.Children.Add(eventStart);
        eventSub.Children.Add(eventTask);
        eventSub.Children.Add(eventEnd);
        eventSub.ChildFlows.Add(new() { Source = eventStart, Target = eventTask });
        eventSub.ChildFlows.Add(new() { Source = eventTask, Target = eventEnd });

        Elements.AddRange([start, catchEvent, work, end, modelBoundary, eventSub]);
        Signals.Add(signal);
        Signals.Add(eventSignal);
        Signals.Add(modelSignal);
        Flows.AddRange([
            new() { Source = start, Target = catchEvent },
            new() { Source = catchEvent, Target = work },
            new() { Source = work, Target = end },
            new() { Source = modelBoundary, Target = eventSub },
            new() { Source = eventSub, Target = end },
        ]);
    }
}
