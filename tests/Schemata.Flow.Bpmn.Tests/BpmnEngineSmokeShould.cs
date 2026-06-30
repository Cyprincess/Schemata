using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Xunit;

namespace Schemata.Flow.Bpmn.Tests;

public class BpmnEngineSmokeShould
{
    [Fact]
    public async Task StartAsync_LinearDefinition_PlacesTokenOnFirstActivity() {
        var definition = LinearDefinition();
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();

        var snapshot = await engine.StartAsync(definition, process, CancellationToken.None);

        Assert.Single(snapshot.Tokens);
        Assert.Single(snapshot.Transitions);
        Assert.Equal("task", snapshot.Tokens[0].StateName);
        Assert.Equal("Active", snapshot.Tokens[0].State);
        Assert.Equal(TransitionKind.Move, snapshot.Transitions[0].Kind);
        Assert.Equal("Start", snapshot.Transitions[0].Event);
        Assert.Equal("Running", snapshot.Process.State);
    }

    [Fact]
    public async Task AdvanceAsync_FromActivityToEnd_CompletesProcess() {
        var definition = LinearDefinition();
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();

        var started  = await engine.StartAsync(definition, process, CancellationToken.None);
        var snapshot = await engine.AdvanceAsync(definition, started.Process, started.Tokens, null, CancellationToken.None);

        Assert.Single(snapshot.Tokens);
        Assert.Single(snapshot.Transitions);
        Assert.Equal("end", snapshot.Tokens[0].StateName);
        Assert.Equal("Completed", snapshot.Tokens[0].State);
        Assert.Equal("Completed", snapshot.Process.State);
        Assert.Equal(TransitionKind.Move, snapshot.Transitions[0].Kind);
    }

    [Fact]
    public async Task AdvanceAsync_CallActivity_ThrowsFailedPrecondition() {
        var definition = WithCallActivity();
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();

        var started = await engine.StartAsync(definition, process, CancellationToken.None);
        var ex = await Assert.ThrowsAsync<FailedPreconditionException>(async () =>
            await engine.AdvanceAsync(definition, started.Process, started.Tokens, null, CancellationToken.None));

        Assert.Equal(SchemataResources.BPMN_CALL_ACTIVITY_REQUIRES_SERVICES, ex.Details?.OfType<ErrorInfoDetail>().FirstOrDefault()?.Reason);
    }

    [Fact]
    public void BpmnValidator_AllowsParallelGateway_DeferringExecutionRejectionToEngine() {
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var gateway  = new ParallelGateway { Name = "fork" };
        var endEvent = new FlowEvent { Name = "end", Position = EventPosition.End };

        var definition = new ProcessDefinition {
            Name     = "permissive",
            Elements = { start, gateway, endEvent },
            Flows = {
                new() { Source = start, Target = gateway },
                new() { Source = gateway, Target = endEvent },
            },
        };

        var ex = Record.Exception(() => BpmnValidator.Validate(definition));
        Assert.Null(ex);
    }

    private static ProcessDefinition LinearDefinition() {
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var task     = new NoneTask { Name = "task" };
        var endEvent = new FlowEvent { Name = "end", Position = EventPosition.End };

        return new() {
            Name     = "linear",
            Elements = { start, task, endEvent },
            Flows = {
                new() { Source = start, Target = task },
                new() { Source = task, Target = endEvent },
            },
        };
    }

    private static ProcessDefinition WithCallActivity() {
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var task     = new NoneTask { Name = "task" };
        var call     = new CallActivity { Name = "call" };
        var endEvent = new FlowEvent { Name = "end", Position = EventPosition.End };

        return new() {
            Name     = "with-callactivity",
            Elements = { start, task, call, endEvent },
            Flows = {
                new() { Source = start, Target = task },
                new() { Source = task, Target = call },
                new() { Source = call, Target = endEvent },
            },
        };
    }

    private static SchemataProcess NewProcess(string definitionName) {
        return new() {
            Name           = "p1",
            CanonicalName  = "processes/p1",
            DefinitionName = definitionName,
        };
    }
}
