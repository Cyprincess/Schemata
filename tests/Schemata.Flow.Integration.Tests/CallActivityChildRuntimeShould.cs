using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.Repository;
using Schemata.Flow.Foundation;
using Schemata.Flow.Integration.Tests.Fixtures;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Integration.Tests;

[Trait("Category", "Integration")]
public class CallActivityChildRuntimeShould : IAsyncLifetime
{
    private readonly RecordingCatchHandler _armed = new();
    private readonly EfCoreFlowFixture _fixture;

    public CallActivityChildRuntimeShould() {
        _fixture = new() { ConfigureServices = services => services.AddSingleton<IFlowCatchHandler>(_armed) };
    }

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task ArmChildMessageCatch_WhenCalledProcessWaits() {
        _fixture.CatchKinds.Add(FlowCatchKind.Message);

        using (var scope = _fixture.CreateScope()) {
            var registry = scope.ServiceProvider.GetRequiredService<IProcessRegistry>();
            await registry.RegisterAsync(new() {
                Name           = "waiting-child",
                Engine         = FlowConstants.Engines.Bpmn,
                DefinitionType = typeof(WaitingChildProcess),
            });
            await registry.RegisterAsync(new() {
                Name           = "calling-parent",
                Engine         = FlowConstants.Engines.Bpmn,
                DefinitionType = typeof(CallingParentProcess),
            });
        }

        SchemataProcess parent;
        using (var scope = _fixture.CreateScope()) {
            var runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
            parent = await runner.StartAsync("calling-parent", null, CancellationToken.None);
        }

        var childArm = Assert.Single(
            _armed.Calls,
            call => call.WaitingAtName == "child-wait" && call.ProcessCanonicalName != parent.CanonicalName);
        Assert.Equal("Waiting", childArm.TokenStatus);

        Assert.Contains(_armed.Calls, call => call.ProcessCanonicalName == parent.CanonicalName && call.WaitingAtName == "call");
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task RepeatedCall_ReloadedParentWaitsForSecondChild() {
        _fixture.CatchKinds.Add(FlowCatchKind.Message);
        using (var scope = _fixture.CreateScope()) {
            var registry = scope.ServiceProvider.GetRequiredService<IProcessRegistry>();
            await registry.RegisterAsync(new() { Name = "waiting-child", Engine = FlowConstants.Engines.Bpmn, DefinitionType = typeof(WaitingChildProcess) });
            await registry.RegisterAsync(new() { Name = "repeating-parent", Engine = FlowConstants.Engines.Bpmn, DefinitionType = typeof(RepeatingParentProcess) });
        }
        SchemataProcess parent;
        using (var scope = _fixture.CreateScope()) {
            parent = await scope.ServiceProvider.GetRequiredService<FlowRunner>().StartAsync("repeating-parent", null, CancellationToken.None);
        }
        var first = Assert.Single(await ReadRowsAsync<SchemataProcess>(), row => row.DefinitionName == "waiting-child");
        using (var scope = _fixture.CreateScope()) {
            await scope.ServiceProvider.GetRequiredService<FlowRunner>().CorrelateAsync(first, "child-message", (string?)null, null, null, CancellationToken.None);
        }
        using (var scope = _fixture.CreateScope()) {
            parent = (await scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcess>>().FirstOrDefaultAsync(query => query.Where(row => row.CanonicalName == parent.CanonicalName)))!;
            var runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
            await runner.CompleteAsync(parent, null, null, CancellationToken.None);
        }
        using (var scope = _fixture.CreateScope()) {
            var current = (await scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcess>>().FirstOrDefaultAsync(query => query.Where(row => row.CanonicalName == parent.CanonicalName)))!;
            await scope.ServiceProvider.GetRequiredService<FlowRunner>().CompleteAsync(current, null, null, CancellationToken.None);
        }
        var children = (await ReadRowsAsync<SchemataProcess>()).Where(row => row.DefinitionName == "waiting-child").ToList();
        var second = Assert.Single(children, row => row.CanonicalName != first.CanonicalName);
        using (var scope = _fixture.CreateScope()) {
            var current = (await scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcess>>().FirstOrDefaultAsync(query => query.Where(row => row.CanonicalName == parent.CanonicalName)))!;
            var snapshot = await scope.ServiceProvider.GetRequiredService<FlowRunner>().CompleteAsync(current, null, null, CancellationToken.None);
            Assert.Equal("call", Assert.Single(snapshot.Tokens, token => token.State == "Waiting").WaitingAtName);
        }
        var spawns = (await ReadRowsAsync<SchemataProcessTransition>()).Where(row => row.Process == parent.Name && row.Kind == TransitionKind.Spawn && row.Note == "call").ToList();
        Assert.Equal(2, spawns.Select(row => row.Token).Distinct().Count());
        Assert.Contains(spawns, row => row.Posterior == first.CanonicalName);
        Assert.Contains(spawns, row => row.Posterior == second.CanonicalName);
        using (var scope = _fixture.CreateScope()) {
            await scope.ServiceProvider.GetRequiredService<FlowRunner>().CorrelateAsync(second, "child-message", (string?)null, null, null, CancellationToken.None);
        }
        using (var scope = _fixture.CreateScope()) {
            var current = (await scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcess>>().FirstOrDefaultAsync(query => query.Where(row => row.CanonicalName == parent.CanonicalName)))!;
            var snapshot = await scope.ServiceProvider.GetRequiredService<FlowRunner>().CompleteAsync(current, null, null, CancellationToken.None);
            Assert.Equal("again", Assert.Single(snapshot.Tokens, token => token.State == "Active").StateName);
        }
    }

    [Trait("Layer", "Integration")]
    [Theory]
    [InlineData(typeof(EndCompensationCallProcess), true)]
    [InlineData(typeof(IntermediateCompensationCallProcess), false)]
    [InlineData(typeof(EndProcedureCompensationCallProcess), true)]
    [InlineData(typeof(IntermediateProcedureCompensationCallProcess), false)]
    public async Task CompensationCall_ReloadedWaiterPreservesThrowerAndCompletesOnce(Type parentType, bool atEnd) {
        _fixture.CatchKinds.Add(FlowCatchKind.Message);
        using (var scope = _fixture.CreateScope()) {
            var registry = scope.ServiceProvider.GetRequiredService<IProcessRegistry>();
            await registry.RegisterAsync(new() { Name = "waiting-child", Engine = FlowConstants.Engines.Bpmn, DefinitionType = typeof(WaitingChildProcess) });
            await registry.RegisterAsync(new() { Name = "compensating-parent", Engine = FlowConstants.Engines.Bpmn, DefinitionType = parentType });
        }
        SchemataProcess parent;
        using (var scope = _fixture.CreateScope()) {
            var runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
            parent = await runner.StartAsync("compensating-parent", null, CancellationToken.None);
            await runner.CompleteAsync(parent, null, null, CancellationToken.None);
        }
        var tokens = (await ReadRowsAsync<SchemataProcessToken>()).Where(row => row.Process == parent.Name).ToList();
        var thrower = Assert.Single(tokens, row => row.StateName == (atEnd ? "throw-end" : "after"));
        var waiter = Assert.Single(tokens, row => row.WaitingAtName == "undo-call" && row.State == "Waiting");
        var carrier = Assert.Single(tokens, row => row.CanonicalName == waiter.Spawner);
        Assert.NotEqual(thrower.CanonicalName, carrier.CanonicalName);
        Assert.Equal(thrower.CanonicalName, carrier.Spawner);
        Assert.Equal("Completed", carrier.State);
        var spawn = Assert.Single(await ReadRowsAsync<SchemataProcessTransition>(), row => row.Process == parent.Name && row.Kind == TransitionKind.Spawn && row.Note == "undo-call");
        Assert.Equal(waiter.CanonicalName, spawn.Token);
        var child = Assert.Single(await ReadRowsAsync<SchemataProcess>(), row => row.CanonicalName == spawn.Posterior);
        using (var scope = _fixture.CreateScope()) {
            await scope.ServiceProvider.GetRequiredService<FlowRunner>().CorrelateAsync(child, "child-message", (string?)null, null, null, CancellationToken.None);
        }
        using (var scope = _fixture.CreateScope()) {
            var current = (await scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcess>>().FirstOrDefaultAsync(query => query.Where(row => row.CanonicalName == parent.CanonicalName)))!;
            await scope.ServiceProvider.GetRequiredService<FlowRunner>().CompleteAsync(current, waiter.CanonicalName, null, CancellationToken.None);
        }
        tokens = (await ReadRowsAsync<SchemataProcessToken>()).Where(row => row.Process == parent.Name).ToList();
        Assert.Equal("Completed", tokens.Single(row => row.CanonicalName == waiter.CanonicalName).State);
        var retained = tokens.Single(row => row.CanonicalName == thrower.CanonicalName);
        Assert.Equal(atEnd ? "throw-end" : "after", retained.StateName);
        Assert.Equal(atEnd ? "Completed" : "Active", retained.State);
        Assert.Single(await ReadRowsAsync<SchemataProcessTransition>(), row => row.Process == parent.Name && row.Token == waiter.CanonicalName && row.Event == "ExitCallActivity");
        Assert.Single(await ReadRowsAsync<SchemataProcessTransition>(), row => row.Process == parent.Name && row.Kind == TransitionKind.Spawn && row.Note == "undo-call");
    }

    private async Task<List<TEntity>> ReadRowsAsync<TEntity>() where TEntity : class {
        using var scope = _fixture.CreateScope();
        var rows = new List<TEntity>();
        await foreach (var row in scope.ServiceProvider.GetRequiredService<IRepository<TEntity>>().ListAsync<TEntity>(query => query)) {
            rows.Add(row);
        }
        return rows;
    }

    public sealed class EndCompensationCallProcess : CompensationCallProcess { public EndCompensationCallProcess() : base(true, false) { } }
    public sealed class IntermediateCompensationCallProcess : CompensationCallProcess { public IntermediateCompensationCallProcess() : base(false, false) { } }
    public sealed class EndProcedureCompensationCallProcess : CompensationCallProcess { public EndProcedureCompensationCallProcess() : base(true, true) { } }
    public sealed class IntermediateProcedureCompensationCallProcess : CompensationCallProcess { public IntermediateProcedureCompensationCallProcess() : base(false, true) { } }

    public abstract class CompensationCallProcess : ProcessDefinition
    {
        protected CompensationCallProcess(bool atEnd, bool procedurePrefix) {
            var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
            var activity = new NoneTask { Name = "activity" };
            var throwing = new FlowEvent { Name = atEnd ? "throw-end" : "throw", Position = atEnd ? EventPosition.End : EventPosition.IntermediateThrow, Definition = new CompensationDefinition { Name = "compensate" } };
            var boundary = new FlowEvent { Name = "compensation-boundary", Position = EventPosition.Boundary, AttachedTo = activity, Definition = new CompensationDefinition { Name = "undo", Activity = activity } };
            var call = new CallActivity { Name = "undo-call", CalledElement = "waiting-child" };
            var callEnd = new FlowEvent { Name = "undo-end", Position = EventPosition.End };
            Elements.AddRange([start, activity, throwing, boundary, call, callEnd]);
            Flows.AddRange([new() { Source = start, Target = activity }, new() { Source = activity, Target = throwing }, new() { Source = call, Target = callEnd }]);
            if (!atEnd) {
                var after = new NoneTask { Name = "after" };
                var end = new FlowEvent { Name = "end", Position = EventPosition.End };
                Elements.AddRange([after, end]);
                Flows.Add(new() { Source = after, Target = end });
                Flows.Add(new() { Source = throwing, Target = after });
            }
            FlowElement target = call;
            if (procedurePrefix) {
                var prefix = new ProcedureTask { Name = "undo-prefix", Body = (_, _) => ValueTask.CompletedTask };
                Elements.Add(prefix);
                Flows.Add(new() { Source = prefix, Target = call });
                target = prefix;
            }
            Flows.Add(new() { Source = boundary, Target = target });
        }
    }

    public sealed class RepeatingParentProcess : ProcessDefinition
    {
        public RepeatingParentProcess() {
            var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
            var call = new CallActivity { Name = "call", CalledElement = "waiting-child" };
            var again = new NoneTask { Name = "again" };
            var end = new FlowEvent { Name = "end", Position = EventPosition.End };
            Elements.AddRange([start, call, again, end]);
            Flows.AddRange([
                new() { Source = start, Target = call }, new() { Source = call, Target = again },
                new() { Source = again, Target = call, Condition = new LambdaConditionExpression { Lambda = _ => new(true) } },
                new() { Source = again, Target = end, IsDefault = true },
            ]);
        }
    }

    private sealed class RecordingCatchHandler : IFlowCatchHandler
    {
        public List<(string ProcessCanonicalName, string? WaitingAtName, string TokenStatus)> Calls { get; } = [];

        public bool Handles(FlowCatchKind kind) { return true; }

        public ValueTask ArmAsync(FlowTransitionContext context, CancellationToken ct = default) {
            if (context.Snapshot.Process.CanonicalName is { } name) {
                Calls.Add((name, context.Token.WaitingAtName, context.Token.Status));
            }

            return default;
        }
    }

    public sealed class CallingParentProcess : ProcessDefinition
    {
        public CallingParentProcess() {
            var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
            var call     = new CallActivity { Name = "call", CalledElement = "waiting-child" };
            var endEvent = new FlowEvent { Name = "end", Position = EventPosition.End };
            Elements.AddRange([start, call, endEvent]);
            Flows.AddRange([new() { Source = start, Target = call }, new() { Source = call, Target = endEvent }]);
        }
    }

    public sealed class WaitingChildProcess : ProcessDefinition
    {
        public WaitingChildProcess() {
            var message = new Message { Name = "child-message" };
            var start   = new FlowEvent { Name = "child-start", Position = EventPosition.Start };
            var wait = new FlowEvent {
                Name       = "child-wait",
                Position   = EventPosition.IntermediateCatch,
                Definition = message,
            };
            var end = new FlowEvent { Name = "child-end", Position = EventPosition.End };
            Elements.AddRange([start, wait, end]);
            Messages.Add(message);
            Flows.AddRange([new() { Source = start, Target = wait }, new() { Source = wait, Target = end }]);
        }
    }
}
