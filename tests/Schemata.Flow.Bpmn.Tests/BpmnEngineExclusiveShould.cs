using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Bpmn.Tests;

public class BpmnEngineExclusiveShould
{
    [Fact]
    public async Task ExclusiveGateway_FirstTrueCondition_TakesThatBranch() {
        var definition = TwoConditionGateway(true, false);
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();

        var started = await engine.StartAsync(definition, process, CancellationToken.None);
        var routed  = await engine.AdvanceAsync(definition, started.Process, started.Tokens, null, CancellationToken.None);

        Assert.Equal("yes-end", routed.Tokens[0].StateName);
        Assert.Equal("Completed", routed.Process.State);
    }

    [Fact]
    public async Task ExclusiveGateway_FirstFalseConditionSecondTrue_TakesSecondBranch() {
        var definition = TwoConditionGateway(false, true);
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();

        var started = await engine.StartAsync(definition, process, CancellationToken.None);
        var routed  = await engine.AdvanceAsync(definition, started.Process, started.Tokens, null, CancellationToken.None);

        Assert.Equal("no-end", routed.Tokens[0].StateName);
        Assert.Equal("Completed", routed.Process.State);
    }

    [Fact]
    public async Task ExclusiveGateway_AllConditionsFalseWithIsDefault_TakesDefault() {
        var definition = WithExplicitDefault(false);
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();

        var started = await engine.StartAsync(definition, process, CancellationToken.None);
        var routed  = await engine.AdvanceAsync(definition, started.Process, started.Tokens, null, CancellationToken.None);

        Assert.Equal("default-end", routed.Tokens[0].StateName);
    }

    [Fact]
    public async Task ExclusiveGateway_AllConditionsFalseWithConditionlessFallback_TakesConditionless() {
        var definition = WithConditionlessFallback(false);
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();

        var started = await engine.StartAsync(definition, process, CancellationToken.None);
        var routed  = await engine.AdvanceAsync(definition, started.Process, started.Tokens, null, CancellationToken.None);

        Assert.Equal("fallback-end", routed.Tokens[0].StateName);
    }

    [Fact]
    public async Task ExclusiveGateway_AllConditionsFalseNoDefault_ThrowsNoOutgoing() {
        var definition = NoDefaultGateway(false, false);
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();

        var started = await engine.StartAsync(definition, process, CancellationToken.None);

        await Assert.ThrowsAsync<FailedPreconditionException>(async () =>
            await engine.AdvanceAsync(definition, started.Process, started.Tokens, null, CancellationToken.None));
    }

    [Fact]
    public async Task ExclusiveGateway_DirectlyAfterStart_RoutesTransparently() {
        var definition = GatewayDirectlyAfterStart(true);
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();

        var started = await engine.StartAsync(definition, process, CancellationToken.None);

        Assert.Equal("yes-task", started.Tokens[0].StateName);
        Assert.Equal("Active", started.Tokens[0].State);
    }

    [Fact]
    public void BpmnValidator_ExclusiveGatewayWithoutOutgoing_Throws() {
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var gateway  = new ExclusiveGateway { Name = "decide" };
        var endEvent = new FlowEvent { Name = "end", Position = EventPosition.End };
        var definition = new ProcessDefinition {
            Name     = "broken-gateway",
            Elements = { start, gateway, endEvent },
            Flows    = { new() { Source = start, Target = gateway } },
        };

        Assert.Throws<FailedPreconditionException>(() => BpmnValidator.Validate(definition));
    }

    private static ProcessDefinition TwoConditionGateway(bool firstTrue, bool secondTrue) {
        var start   = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var task    = new NoneTask { Name = "task" };
        var gateway = new ExclusiveGateway { Name = "decide" };
        var yesEnd  = new FlowEvent { Name = "yes-end", Position = EventPosition.End };
        var noEnd   = new FlowEvent { Name = "no-end", Position = EventPosition.End };

        return new() {
            Name     = "two-conditions",
            Elements = { start, task, gateway, yesEnd, noEnd },
            Flows = {
                new() { Source = start, Target = task },
                new() { Source = task, Target = gateway },
                new() { Source = gateway, Target = yesEnd, Condition = Const(firstTrue) },
                new() { Source = gateway, Target = noEnd, Condition  = Const(secondTrue) },
            },
        };
    }

    private static ProcessDefinition WithExplicitDefault(bool firstTrue) {
        var start      = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var task       = new NoneTask { Name = "task" };
        var gateway    = new ExclusiveGateway { Name = "decide" };
        var matchEnd   = new FlowEvent { Name = "match-end", Position = EventPosition.End };
        var defaultEnd = new FlowEvent { Name = "default-end", Position = EventPosition.End };

        return new() {
            Name     = "explicit-default",
            Elements = { start, task, gateway, matchEnd, defaultEnd },
            Flows = {
                new() { Source = start, Target = task },
                new() { Source = task, Target = gateway },
                new() { Source = gateway, Target = matchEnd, Condition   = Const(firstTrue) },
                new() { Source = gateway, Target = defaultEnd, IsDefault = true },
            },
        };
    }

    private static ProcessDefinition WithConditionlessFallback(bool firstTrue) {
        var start       = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var task        = new NoneTask { Name = "task" };
        var gateway     = new ExclusiveGateway { Name = "decide" };
        var matchEnd    = new FlowEvent { Name = "match-end", Position = EventPosition.End };
        var fallbackEnd = new FlowEvent { Name = "fallback-end", Position = EventPosition.End };

        return new() {
            Name     = "implicit-default",
            Elements = { start, task, gateway, matchEnd, fallbackEnd },
            Flows = {
                new() { Source = start, Target = task },
                new() { Source = task, Target = gateway },
                new() { Source = gateway, Target = matchEnd, Condition = Const(firstTrue) },
                new() { Source = gateway, Target = fallbackEnd },
            },
        };
    }

    private static ProcessDefinition NoDefaultGateway(bool firstTrue, bool secondTrue) {
        var start   = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var task    = new NoneTask { Name = "task" };
        var gateway = new ExclusiveGateway { Name = "decide" };
        var yesEnd  = new FlowEvent { Name = "yes-end", Position = EventPosition.End };
        var noEnd   = new FlowEvent { Name = "no-end", Position = EventPosition.End };

        return new() {
            Name     = "no-default",
            Elements = { start, task, gateway, yesEnd, noEnd },
            Flows = {
                new() { Source = start, Target = task },
                new() { Source = task, Target = gateway },
                new() { Source = gateway, Target = yesEnd, Condition = Const(firstTrue) },
                new() { Source = gateway, Target = noEnd, Condition  = Const(secondTrue) },
            },
        };
    }

    private static ProcessDefinition GatewayDirectlyAfterStart(bool takeYes) {
        var start   = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var gateway = new ExclusiveGateway { Name = "decide" };
        var yes     = new NoneTask { Name = "yes-task" };
        var no      = new NoneTask { Name = "no-task" };
        var endA    = new FlowEvent { Name = "end-a", Position = EventPosition.End };
        var endB    = new FlowEvent { Name = "end-b", Position = EventPosition.End };

        return new() {
            Name     = "gateway-after-start",
            Elements = { start, gateway, yes, no, endA, endB },
            Flows = {
                new() { Source = start, Target = gateway },
                new() { Source = gateway, Target = yes, Condition = Const(takeYes) },
                new() { Source = gateway, Target = no, IsDefault  = true },
                new() { Source = yes, Target = endA },
                new() { Source = no, Target = endB },
            },
        };
    }

    private static IConditionExpression Const(bool value) {
        return new LambdaConditionExpression { Lambda = _ => new(value) };
    }

    private static SchemataProcess NewProcess(string definitionName) {
        return new() {
            Name           = "p1",
            CanonicalName  = "processes/p1",
            DefinitionName = definitionName,
        };
    }
}
