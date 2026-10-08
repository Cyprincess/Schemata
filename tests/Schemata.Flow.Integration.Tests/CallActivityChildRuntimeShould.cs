using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
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
