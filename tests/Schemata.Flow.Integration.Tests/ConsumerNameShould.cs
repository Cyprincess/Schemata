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
            Assert.Equal(callActivity ? 3 : 2, transitions.Count);
            Assert.All(tokens, token => {
                Assert.StartsWith("consumer-", token.Name);
                Assert.NotEqual(token.Uid.ToString("n"), token.Name);
                Assert.Equal($"{token.Process}/tokens/{token.Name}", token.CanonicalName);
                Assert.Contains(processes, owner => owner.CanonicalName == token.Process);
                if (token.Spawner is { } spawner) {
                    Assert.Contains(tokens, ancestor => ancestor.CanonicalName == spawner && ancestor.Process == token.Process);
                }
            });
            Assert.All(transitions, transition => {
                Assert.StartsWith("consumer-", transition.Name);
                Assert.Contains(tokens, token => token.CanonicalName == transition.Token);
                var owner = Assert.Single(processes, process => process.CanonicalName == transition.Process);
                Assert.NotEqual(owner.Name, transition.Process);
                Assert.Equal($"{owner.CanonicalName}/transitions/{transition.Name}", transition.CanonicalName);
            });

            var parent = Assert.Single(tokens, token => token.Process == process.CanonicalName && token.State == "Waiting");
            SchemataProcess workProcess;
            SchemataProcessToken work;
            if (callActivity) {
                Assert.Equal("call", parent.WaitingAtName);
                var consumed = Assert.Single(tokens, token => token.CanonicalName == parent.Spawner);
                Assert.Equal("Completed", consumed.State);
                Assert.Null(consumed.WaitingAtName);
                Assert.Null(consumed.Spawner);
                Assert.Equal(string.Empty, parent.ScopeName);
                workProcess = Assert.Single(processes, row => row.DefinitionName == nameof(CalledProcess));
                work = Assert.Single(tokens, token => token.Process == workProcess.CanonicalName);
                var spawn = Assert.Single(transitions, transition => transition.Kind == TransitionKind.Spawn);
                Assert.Equal(parent.CanonicalName, spawn.Token);
                Assert.Equal(workProcess.CanonicalName, spawn.Posterior);
            } else {
                Assert.Null(parent.Spawner);
                Assert.Equal(string.Empty, parent.ScopeName);
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
            Assert.Single(resumed, token => TokenStates.Live.Contains(token.State!) && token.Process == process.CanonicalName && token.CanonicalName == parent.CanonicalName);
            Assert.Equal(tokens.Select(token => token.CanonicalName).OrderBy(name => name), resumed.Select(token => token.CanonicalName).OrderBy(name => name));
            Assert.All(resumed, token => {
                Assert.StartsWith("consumer-", token.Name);
                Assert.Equal($"{token.Process}/tokens/{token.Name}", token.CanonicalName);
            });
            if (callActivity) {
                Assert.Equal("Completed", Assert.Single(resumed, token => token.CanonicalName == parent.Spawner).State);
            }
        } finally {
            await lifetime.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(false, FlowConstants.Engines.StateMachine)]
    [InlineData(true, FlowConstants.Engines.StateMachine)]
    [InlineData(false, FlowConstants.Engines.Bpmn)]
    [InlineData(true, FlowConstants.Engines.Bpmn)]
    public async Task Cancel_Reloaded_Token_Uses_Canonical_Owner_And_Isolates_Other_Process(bool linq, string engine) {
        IFlowIntegrationFixture fixture = linq ? new LinqToDbFlowFixture() : new EfCoreFlowFixture();
        var lifetime = (IAsyncLifetime)fixture;
        await lifetime.InitializeAsync();
        try {
            SchemataProcess first;
            SchemataProcess second;
            using (var start = fixture.CreateScope()) {
                await start.ServiceProvider.GetRequiredService<IProcessRegistry>().RegisterAsync<ProcessVersionShould.Original>(engine);
                var runner = start.ServiceProvider.GetRequiredService<FlowRunner>();
                first = await runner.StartAsync(nameof(ProcessVersionShould.Original));
                second = await runner.StartAsync(nameof(ProcessVersionShould.Original));
            }
            var tokens = await Rows<SchemataProcessToken>(fixture);
            var target = Assert.Single(tokens, row => row.Process == first.CanonicalName);
            Assert.NotEqual(first.Name, target.Process);
            Assert.Single(tokens, row => row.Process == second.CanonicalName);
            using (var cancel = fixture.CreateScope()) {
                var result = await cancel.ServiceProvider.GetRequiredService<FlowRunner>().CancelTokenAsync(target, null, default);
                Assert.Equal(first.CanonicalName, result.Process.CanonicalName);
            }
            var reloaded = await Rows<SchemataProcessToken>(fixture);
            Assert.Equal("Cancelled", Assert.Single(reloaded, row => row.Process == first.CanonicalName).State);
            Assert.Equal("Active", Assert.Single(reloaded, row => row.Process == second.CanonicalName).State);
            var transitions = await Rows<SchemataProcessTransition>(fixture);
            Assert.Contains(transitions, row => row.Process == first.CanonicalName && row.Kind == TransitionKind.Cancel && row.Token == target.CanonicalName);
            Assert.DoesNotContain(transitions, row => row.Process == second.CanonicalName && row.Kind == TransitionKind.Cancel);
            Assert.All(transitions, row => Assert.Contains(row.Process, new[] { first.CanonicalName, second.CanonicalName }));
        } finally { await lifetime.DisposeAsync(); }
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
            var fail = new ProcedureTask { Name = "fail", Body = (_, ct) => { ct.ThrowIfCancellationRequested(); throw new InvalidOperationException("entry failed"); } };
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
