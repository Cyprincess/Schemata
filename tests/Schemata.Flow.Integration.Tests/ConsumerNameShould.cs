using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.Repository;
using Schemata.Flow.Foundation;
using Schemata.Flow.Integration.Tests.Fixtures;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class ConsumerNameShould
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Reload_And_Resume_Spawned_Work_Using_Consumer_Assigned_Names(bool linqToDb, bool callActivity) {
        IFlowIntegrationFixture fixture = linqToDb ? new LinqToDbFlowFixture() : new EfCoreFlowFixture();
        var lifetime = (IAsyncLifetime)fixture;
        await lifetime.InitializeAsync();
        try {
            SchemataProcess process;
            using (var scope = fixture.CreateScope()) {
                var registry = scope.ServiceProvider.GetRequiredService<IProcessRegistry>();
                await registry.RegisterAsync<CalledProcess>(FlowConstants.Engines.Bpmn);
                await registry.RegisterAsync<CallingProcess>(FlowConstants.Engines.Bpmn);
                await registry.RegisterAsync<NestedProcess>(FlowConstants.Engines.Bpmn);
                process = await scope.ServiceProvider.GetRequiredService<FlowRunner>()
                                     .StartAsync(callActivity ? nameof(CallingProcess) : nameof(NestedProcess));
            }

            Assert.StartsWith("consumer-", process.Name);
            Assert.NotEqual(process.Uid.ToString("n"), process.Name);
            var processes = await Rows<SchemataProcess>(fixture);
            var tokens = await Rows<SchemataProcessToken>(fixture);
            var transitions = await Rows<SchemataProcessTransition>(fixture);
            Assert.Equal(callActivity ? 2 : 1, processes.Count);
            Assert.Equal(2, tokens.Count);
            Assert.Equal(callActivity ? 3 : 2, transitions.Count);
            Assert.All(tokens, token => {
                Assert.StartsWith("consumer-", token.Name);
                Assert.NotEqual(token.Uid.ToString("n"), token.Name);
                Assert.Equal($"processes/{token.Process}/tokens/{token.Name}", token.CanonicalName);
                Assert.Contains(processes, owner => owner.Name == token.Process);
            });
            Assert.All(transitions, transition => {
                Assert.StartsWith("consumer-", transition.Name);
                Assert.Contains(tokens, token => token.CanonicalName == transition.Token);
            });

            var parent = Assert.Single(tokens, token => token.Process == process.Name && token.Spawner is null);
            SchemataProcess workProcess;
            SchemataProcessToken work;
            if (callActivity) {
                workProcess = Assert.Single(processes, row => row.DefinitionName == nameof(CalledProcess));
                work = Assert.Single(tokens, token => token.Process == workProcess.Name);
                var spawn = Assert.Single(transitions, transition => transition.Kind == TransitionKind.Spawn);
                Assert.Equal(parent.CanonicalName, spawn.Token);
                Assert.Equal(workProcess.CanonicalName, spawn.Posterior);
            } else {
                workProcess = process;
                work = Assert.Single(tokens, token => token.Spawner == parent.CanonicalName);
                Assert.Equal("nested", work.ScopeName);
                Assert.Equal("nested", parent.WaitingAtName);
            }

            await Complete(fixture, workProcess, work.CanonicalName!);
            if (callActivity) {
                await Complete(fixture, process, parent.CanonicalName!);
            }
            var resumed = await Rows<SchemataProcessToken>(fixture);
            Assert.Equal("after", Assert.Single(resumed, token => token.CanonicalName == parent.CanonicalName).StateName);
            Assert.Equal("Completed", Assert.Single(resumed, token => token.CanonicalName == work.CanonicalName).State);
            Assert.Equal(2, resumed.Count);
        } finally {
            await lifetime.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Roll_Back_Consumer_Named_Process_And_Token_When_Entry_Task_Fails(bool linqToDb) {
        IFlowIntegrationFixture fixture = linqToDb ? new LinqToDbFlowFixture() : new EfCoreFlowFixture();
        var lifetime = (IAsyncLifetime)fixture;
        await lifetime.InitializeAsync();
        try {
            using (var scope = fixture.CreateScope()) {
                await scope.ServiceProvider.GetRequiredService<IProcessRegistry>()
                           .RegisterAsync<FailingEntryProcess>(FlowConstants.Engines.Bpmn);
                var runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await runner.StartAsync(nameof(FailingEntryProcess)));
            }
            Assert.Empty(await Rows<SchemataProcess>(fixture));
            Assert.Empty(await Rows<SchemataProcessToken>(fixture));
            Assert.Empty(await Rows<SchemataProcessTransition>(fixture));
        } finally {
            await lifetime.DisposeAsync();
        }
    }

    private static async Task Complete(IFlowIntegrationFixture fixture, SchemataProcess process, string token) {
        using var scope = fixture.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcess>>();
        var current = await repository.FirstOrDefaultAsync(query => query.Where(row => row.CanonicalName == process.CanonicalName));
        await scope.ServiceProvider.GetRequiredService<FlowRunner>().CompleteAsync(current!, token, null, default);
    }

    private static async Task<List<TEntity>> Rows<TEntity>(IFlowIntegrationFixture fixture) where TEntity : class {
        using var scope = fixture.CreateScope();
        var rows = new List<TEntity>();
        await foreach (var row in scope.ServiceProvider.GetRequiredService<IRepository<TEntity>>().ListAsync<TEntity>(null)) {
            rows.Add(row);
        }
        return rows;
    }

    public sealed class FailingEntryProcess : ProcessDefinition
    {
        public FailingEntryProcess() {
            var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
            var fail = new ProcedureTask { Name = "fail", Body = _ => throw new InvalidOperationException("entry failed") };
            var end = new FlowEvent { Name = "end", Position = EventPosition.End };
            Elements.AddRange([start, fail, end]);
            Flows.AddRange([new() { Source = start, Target = fail }, new() { Source = fail, Target = end }]);
        }
    }

    public sealed class CalledProcess : ProcessDefinition
    {
        public CalledProcess() {
            var start = new FlowEvent { Name = "child-start", Position = EventPosition.Start };
            var work = new NoneTask { Name = "work" };
            var end = new FlowEvent { Name = "child-end", Position = EventPosition.End };
            Elements.AddRange([start, work, end]);
            Flows.AddRange([new() { Source = start, Target = work }, new() { Source = work, Target = end }]);
        }
    }

    public sealed class CallingProcess : ProcessDefinition
    {
        public CallingProcess() {
            var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
            var call = new CallActivity { Name = "call", CalledElement = nameof(CalledProcess) };
            var after = new NoneTask { Name = "after" };
            var end = new FlowEvent { Name = "end", Position = EventPosition.End };
            Elements.AddRange([start, call, after, end]);
            Flows.AddRange([new() { Source = start, Target = call }, new() { Source = call, Target = after }, new() { Source = after, Target = end }]);
        }
    }

    public sealed class NestedProcess : ProcessDefinition
    {
        public NestedProcess() {
            var innerStart = new FlowEvent { Name = "inner-start", Position = EventPosition.Start };
            var work = new NoneTask { Name = "work" };
            var innerEnd = new FlowEvent { Name = "inner-end", Position = EventPosition.End };
            var nested = new EmbeddedSubProcess { Name = "nested" };
            nested.Children.AddRange([innerStart, work, innerEnd]);
            nested.ChildFlows.AddRange([new() { Source = innerStart, Target = work }, new() { Source = work, Target = innerEnd }]);
            var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
            var after = new NoneTask { Name = "after" };
            var end = new FlowEvent { Name = "end", Position = EventPosition.End };
            Elements.AddRange([start, nested, after, end]);
            Flows.AddRange([new() { Source = start, Target = nested }, new() { Source = nested, Target = after }, new() { Source = after, Target = end }]);
        }
    }
}
