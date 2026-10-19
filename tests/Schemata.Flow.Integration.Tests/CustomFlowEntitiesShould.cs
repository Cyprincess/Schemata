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
            var processName = process.Name!;
            var processCanonical = process.CanonicalName!;

            await using (var scope = root.CreateAsyncScope()) {
                var token = await SingleTokenAsync(scope.ServiceProvider, processName);
                Assert.Equal("signal-catch", token.WaitingAtName);
                Assert.Equal("Waiting", token.State);
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
                Assert.NotEmpty(transitions);
                Assert.All(transitions, transition => {
                    Assert.IsType<TenantProcessTransition>(transition);
                    Assert.Equal(token.CanonicalName, transition.Token);
                });
                Assert.Contains(transitions, transition => transition.Posterior == "signal-catch");
                Assert.Contains(transitions, transition => transition.Posterior == "work");

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
        await foreach (var token in tokens.ListAsync<TenantProcessToken>(query => query.Where(t => t.Process == processName))) {
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

    public TenantSignalProcess() {
        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var signal = new Signal { Name = SignalName };
        var catchEvent = new FlowEvent {
            Name       = "signal-catch",
            Position   = EventPosition.IntermediateCatch,
            Definition = signal,
        };
        var work = new NoneTask { Name = "work" };
        var end  = new FlowEvent { Name = "end", Position = EventPosition.End };

        Elements.AddRange([start, catchEvent, work, end]);
        Signals.Add(signal);
        Flows.AddRange([
            new() { Source = start, Target = catchEvent },
            new() { Source = catchEvent, Target = work },
            new() { Source = work, Target = end },
        ]);
    }
}
