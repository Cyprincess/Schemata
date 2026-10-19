using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Utilities;
using Xunit;

namespace Schemata.Flow.Bpmn.Tests;

public class BpmnEngine_TerminateShould
{
    [Trait("Layer", "Unit")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fork_FirstAutomaticTerminate_PreventsLaterBranchEffects(bool fromToken) {
        var calls = 0;
        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var setup = new NoneTask { Name = "setup" };
        var fork = new ParallelGateway { Name = "fork" };
        var first = new ProcedureTask { Name = "first", Body = (_, _) => ValueTask.CompletedTask };
        var later = new ProcedureTask { Name = "later", Body = (_, _) => { calls++; return ValueTask.CompletedTask; } };
        var end = new FlowEvent { Name = "terminate", Position = EventPosition.End, IsTerminate = true };
        var definition = new ProcessDefinition { Name = "fork-terminate", Elements = { start, setup, fork, first, later, end }, Flows = {
            new() { Source = start, Target = fromToken ? setup : fork },
            new() { Source = fork, Target = first }, new() { Source = fork, Target = later },
            new() { Source = first, Target = end },
        } };
        if (fromToken) {
            definition.Flows.Add(new() { Source = setup, Target = fork });
        }
        var engine = new BpmnEngine();
        var snapshot = await engine.StartAsync(definition, NewProcess(definition.Name), CancellationToken.None);
        if (fromToken) {
            snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, snapshot.Tokens.Single().CanonicalName, CancellationToken.None);
        }
        Assert.Equal(0, calls);
        Assert.Equal("Terminated", snapshot.Process.State);
        Assert.DoesNotContain(snapshot.Tokens, token => token.State is "Active" or "Waiting");
        Assert.Equal("Cancelled", snapshot.Tokens.Single(token => token.StateName == "later").State);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async Task ScopedFork_AutomaticTerminate_CancelsStagedSiblingAndResumesParent() {
        var calls = 0;
        var definition = SubProcessTerminateDefinition();
        var sub = (SubProcess)definition.FindElementByName("sub")!;
        var fork = sub.Children.OfType<ParallelGateway>().Single();
        var terminate = sub.Children.OfType<FlowEvent>().Single(value => value.IsTerminate);
        var first = new ProcedureTask { Name = "auto-first", Body = (_, _) => ValueTask.CompletedTask };
        var later = new ProcedureTask { Name = "auto-later", Body = (_, _) => { calls++; return ValueTask.CompletedTask; } };
        sub.Children.Add(first);
        sub.Children.Add(later);
        var branches = sub.ChildFlows.Where(flow => flow.Source == fork).ToArray();
        branches[0].Target = first;
        branches[1].Target = later;
        sub.ChildFlows.Add(new() { Source = first, Target = terminate });
        var engine = new BpmnEngine();
        var snapshot = await engine.StartAsync(definition, NewProcess(definition.Name), CancellationToken.None);
        snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, snapshot.Tokens.Single(token => token.StateName == "setup").CanonicalName, CancellationToken.None);
        snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, snapshot.Tokens.Single(token => token.StateName == "inner-prep").CanonicalName, CancellationToken.None);
        Assert.Equal(0, calls);
        Assert.Equal("Cancelled", snapshot.Tokens.Single(token => token.StateName == "auto-later").State);
        Assert.Equal("Active", snapshot.Tokens.Single(token => token.StateName == "after-sub").State);
        Assert.Equal("Running", snapshot.Process.State);
    }

    [Fact]
    public async Task EndEvent_ProcessLevelTerminate_CancelsParallelSiblingTokens() {
        var definition = ProcessLevelTerminateDefinition();
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();

        var snapshot = await engine.StartAsync(definition, process, CancellationToken.None);
        var killer   = snapshot.Tokens.Single(t => t.StateName == "killer");
        snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, killer.CanonicalName, CancellationToken.None);

        Assert.Equal("Terminated", snapshot.Process.State);
        Assert.Equal("Completed", snapshot.Tokens.Single(t => t.StateName == "terminate").State);
        Assert.Equal("Cancelled", snapshot.Tokens.Single(t => t.StateName == "sibling").State);
        Assert.Contains(snapshot.Transitions, t => t is { Kind: TransitionKind.Cancel, Posterior: "terminate" });
        Assert.DoesNotContain(snapshot.Transitions, t => t.Kind == TransitionKind.Compensate);
        Assert.DoesNotContain(snapshot.Tokens, t => t.StateName == "boundary-handler" || t.StateName == "event-handler");
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async Task ProcedureToTerminate_CancelsParallelSiblingTokens() {
        var definition = ProcessLevelTerminateDefinition();
        var killer = definition.FindElementByName("killer")!;
        var terminate = definition.FindElementByName("terminate")!;
        var procedure = new ProcedureTask { Name = "procedure", Body = (_, _) => ValueTask.CompletedTask };
        definition.Elements.Add(procedure);
        definition.Flows.Single(flow => flow.Source == killer).Target = procedure;
        definition.Flows.Add(new() { Source = procedure, Target = terminate });
        var engine = new BpmnEngine();
        var snapshot = await engine.StartAsync(definition, NewProcess(definition.Name), CancellationToken.None);
        snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, snapshot.Tokens.Single(token => token.StateName == "killer").CanonicalName, CancellationToken.None);
        Assert.Equal("Terminated", snapshot.Process.State);
        Assert.Equal("Cancelled", snapshot.Tokens.Single(token => token.StateName == "sibling").State);
        Assert.Contains(snapshot.Transitions, transition => transition.Previous == "procedure" && transition.Posterior == "terminate");
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async Task CatchToTerminate_CancelsParallelSiblingTokens() {
        var definition = ProcessLevelTerminateDefinition();
        var killer = definition.FindElementByName("killer")!;
        var signal = new Signal { Name = "stop" };
        var catchEvent = new FlowEvent { Name = "stop-catch", Position = EventPosition.IntermediateCatch, Definition = signal };
        definition.Elements.Add(catchEvent);
        definition.Flows.Single(flow => flow.Target == killer).Target = catchEvent;
        definition.Flows.Single(flow => flow.Source == killer).Source = catchEvent;
        var engine = new BpmnEngine();
        var snapshot = await engine.StartAsync(definition, NewProcess(definition.Name), CancellationToken.None);
        var waiting = snapshot.Tokens.Single(token => token.WaitingAtName == catchEvent.Name);
        snapshot = await engine.TriggerAsync(definition, snapshot.Process, snapshot.Tokens, signal, null, waiting.CanonicalName, CancellationToken.None);
        Assert.Equal("Terminated", snapshot.Process.State);
        Assert.Equal("Cancelled", snapshot.Tokens.Single(token => token.StateName == "sibling").State);
        Assert.Contains(snapshot.Transitions, transition => transition.Kind == TransitionKind.Cancel && transition.Posterior == "terminate");
    }

    [Fact]
    public async Task EndEvent_SubProcessTerminate_CancelsOnlySubProcessScopeAndResumesParent() {
        var definition = SubProcessTerminateDefinition();
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();

        var snapshot = await engine.StartAsync(definition, process, CancellationToken.None);
        snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, null, CancellationToken.None);
        var prep = snapshot.Tokens.Single(t => t.StateName == "inner-prep");
        snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, prep.CanonicalName, CancellationToken.None);

        var killer = snapshot.Tokens.Single(t => t.StateName == "inner-killer");
        snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, killer.CanonicalName, CancellationToken.None);

        Assert.Equal("Running", snapshot.Process.State);
        Assert.Equal("Completed", snapshot.Tokens.Single(t => t.StateName == "inner-terminate").State);
        Assert.Equal("Cancelled", snapshot.Tokens.Single(t => t.StateName == "inner-sibling").State);

        var parent = snapshot.Tokens.Single(t => string.IsNullOrEmpty(t.ScopeName));
        Assert.Equal("after-sub", parent.StateName);
        Assert.Equal("Active", parent.State);
        Assert.DoesNotContain(snapshot.Tokens, t => t.StateName == "boundary-handler" || t.StateName == "event-handler");
    }

    private static ProcessDefinition ProcessLevelTerminateDefinition() {
        var start     = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var fork      = new ParallelGateway { Name = "fork" };
        var killer    = new NoneTask { Name = "killer" };
        var sibling   = new NoneTask { Name = "sibling" };
        var terminate = new FlowEvent { Name = "terminate", Position = EventPosition.End, IsTerminate = true };
        var siblingEnd = new FlowEvent { Name = "sibling-end", Position = EventPosition.End };
        var boundary = new FlowEvent {
            Name = "sibling-boundary", Position = EventPosition.Boundary,
            AttachedTo = sibling, Definition = new EscalationDefinition { Name = "SiblingEscalation" }, Interrupting = false,
        };
        var boundaryHandler = new NoneTask { Name = "boundary-handler" };

        return new() {
            Name = "process-terminate",
            Elements = { start, fork, killer, sibling, terminate, siblingEnd, boundary, boundaryHandler },
            Flows = {
                new() { Source = start, Target = fork },
                new() { Source = fork, Target = killer },
                new() { Source = fork, Target = sibling },
                new() { Source = killer, Target = terminate },
                new() { Source = sibling, Target = siblingEnd },
                new() { Source = boundary, Target = boundaryHandler },
            },
        };
    }

    private static ProcessDefinition SubProcessTerminateDefinition() {
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var setup    = new NoneTask { Name = "setup" };
        var sub      = new EmbeddedSubProcess { Name = "sub" };
        var afterSub = new NoneTask { Name = "after-sub" };
        var end      = new FlowEvent { Name = "end", Position = EventPosition.End };

        var innerStart = new FlowEvent { Name = "inner-start", Position = EventPosition.Start };
        var innerPrep  = new NoneTask { Name = "inner-prep" };
        var innerFork  = new ParallelGateway { Name = "inner-fork" };
        var killer     = new NoneTask { Name = "inner-killer" };
        var sibling    = new NoneTask { Name = "inner-sibling" };
        var terminate  = new FlowEvent { Name = "inner-terminate", Position = EventPosition.End, IsTerminate = true };
        var siblingEnd = new FlowEvent { Name = "inner-sibling-end", Position = EventPosition.End };
        var boundary = new FlowEvent {
            Name = "inner-boundary", Position = EventPosition.Boundary,
            AttachedTo = sibling, Definition = new EscalationDefinition { Name = "InnerEscalation" }, Interrupting = false,
        };
        var boundaryHandler = new NoneTask { Name = "boundary-handler" };
        var eventSub = new EventSubProcess { Name = "event-sub" };
        var eventStart = new FlowEvent {
            Name = "event-start", Position = EventPosition.Start,
            Definition = new EscalationDefinition { Name = "InnerEscalation" }, Interrupting = false,
        };
        var eventHandler = new NoneTask { Name = "event-handler" };

        eventSub.Children.Add(eventStart);
        eventSub.Children.Add(eventHandler);
        eventSub.ChildFlows.Add(new() { Source = eventStart, Target = eventHandler });

        sub.Children.Add(innerStart);
        sub.Children.Add(innerPrep);
        sub.Children.Add(innerFork);
        sub.Children.Add(killer);
        sub.Children.Add(sibling);
        sub.Children.Add(terminate);
        sub.Children.Add(siblingEnd);
        sub.Children.Add(boundary);
        sub.Children.Add(boundaryHandler);
        sub.Children.Add(eventSub);
        sub.ChildFlows.Add(new() { Source = innerStart, Target = innerPrep });
        sub.ChildFlows.Add(new() { Source = innerPrep, Target = innerFork });
        sub.ChildFlows.Add(new() { Source = innerFork, Target = killer });
        sub.ChildFlows.Add(new() { Source = innerFork, Target = sibling });
        sub.ChildFlows.Add(new() { Source = killer, Target = terminate });
        sub.ChildFlows.Add(new() { Source = sibling, Target = siblingEnd });
        sub.ChildFlows.Add(new() { Source = boundary, Target = boundaryHandler });

        return new() {
            Name = "subprocess-terminate",
            Elements = { start, setup, sub, afterSub, end },
            Flows = {
                new() { Source = start, Target = setup },
                new() { Source = setup, Target = sub },
                new() { Source = sub, Target = afterSub },
                new() { Source = afterSub, Target = end },
            },
        };
    }

    private static SchemataProcess NewProcess(string definitionName) {
        return new() {
            Name = "p1", CanonicalName = "processes/p1", DefinitionName = definitionName,
        };
    }
}
