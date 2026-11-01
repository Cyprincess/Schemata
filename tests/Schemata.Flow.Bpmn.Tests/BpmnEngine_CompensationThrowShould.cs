using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Xunit;

namespace Schemata.Flow.Bpmn.Tests;

public class BpmnEngine_CompensationThrowShould
{
    [Fact]
    public async Task Throw_IntermediateThrowGlobalCompensation_FiresAllHandlersInScope() {
        var scenario = CompensationScenario(3, null, false);
        var snapshot = await AdvanceIntoThrowAsync(scenario);

        var compensations = snapshot.Transitions.Where(t => t.Kind == TransitionKind.Compensate).ToList();
        Assert.Equal(3, compensations.Count);
        Assert.Equal(["undo-C", "undo-B", "undo-A"], compensations.Select(t => t.Posterior));
    }

    [Fact]
    public async Task Throw_IntermediateThrowTargetedCompensation_FiresOnlyOneHandler() {
        var scenario = CompensationScenario(3, "A", false);
        var snapshot = await AdvanceIntoThrowAsync(scenario);

        var transition = Assert.Single(snapshot.Transitions, t => t.Kind == TransitionKind.Compensate);
        Assert.Equal("A", transition.Previous);
        Assert.Equal("undo-A", transition.Posterior);
    }

    [Fact]
    public async Task Throw_IntermediateThrow_TokenContinuesPastEvent() {
        var scenario = CompensationScenario(3, null, false);
        var snapshot = await AdvanceIntoThrowAsync(scenario);

        var token = Assert.Single(snapshot.Tokens);
        Assert.Equal("after", token.StateName);
        Assert.Equal("Active", token.State);
        Assert.Contains(snapshot.Transitions, t => t is { Kind: TransitionKind.Move, Previous: "throw", Posterior: "after" });
    }

    [Fact]
    public async Task Throw_EndEventCompensation_FiresAndTokenConsumed() {
        var scenario = CompensationScenario(3, null, true);
        var snapshot = await AdvanceIntoThrowAsync(scenario);

        var token = Assert.Single(snapshot.Tokens);
        Assert.Equal("throw-end", token.StateName);
        Assert.Equal("Completed", token.State);
        Assert.Equal("Completed", snapshot.Process.State);
        Assert.Equal(3, snapshot.Transitions.Count(t => t.Kind == TransitionKind.Compensate));
    }

    [Fact]
    public async Task Throw_GlobalCompensationWithFiveActivities_ReverseOrderStrictEquality() {
        var scenario = CompensationScenario(5, null, false);
        var snapshot = await AdvanceIntoThrowAsync(scenario);

        var order = snapshot.Transitions
                            .Where(t => t.Kind == TransitionKind.Compensate)
                            .Select(t => t.Previous)
                            .ToList();
        Assert.Equal(new[] { "E", "D", "C", "B", "A" }, order);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async Task ProcedureToCompensationEnd_InvokesRegisteredHandler() {
        var scenario = CompensationScenario(1, null, true);
        var flow = scenario.Definition.Flows.Single(value => value.Source.Name == "A");
        var end = flow.Target;
        var procedure = new ProcedureTask { Name = "before-throw", Body = (_, _) => ValueTask.CompletedTask };
        scenario.Definition.Elements.Add(procedure);
        flow.Target = procedure;
        scenario.Definition.Flows.Add(new() { Source = procedure, Target = end });
        var snapshot = await AdvanceIntoThrowAsync(scenario);
        Assert.Single(snapshot.Transitions, transition => transition.Kind == TransitionKind.Compensate && transition.Posterior == "undo-A");
        Assert.Equal("Completed", snapshot.Process.State);
        Assert.Equal("throw-end", Assert.Single(snapshot.Tokens).StateName);
    }

    [Trait("Layer", "Unit")]
    [Theory]
    [InlineData(true, "direct")]
    [InlineData(false, "direct")]
    [InlineData(true, "procedure")]
    [InlineData(false, "procedure")]
    [InlineData(true, "gateway")]
    [InlineData(false, "gateway")]
    public async Task Compensation_SubProcessOwnerSurvivesThrowerContinuation(bool atEnd, string route) {
        var scenario = CompensationScenario(1, null, atEnd);
        var innerStart = new FlowEvent { Name = "undo-start", Position = EventPosition.Start };
        var task = new NoneTask { Name = "undo-task" };
        var innerEnd = new FlowEvent { Name = "undo-end", Position = EventPosition.End };
        var sub = new EmbeddedSubProcess { Name = "undo-sub", Children = { innerStart, task, innerEnd }, ChildFlows = {
            new() { Source = innerStart, Target = task }, new() { Source = task, Target = innerEnd },
        } };
        scenario.Definition.Elements.Add(sub);
        FlowElement target = sub;
        if (route != "direct") {
            target = route == "procedure"
                ? new ProcedureTask { Name = "undo-prefix", Body = (_, _) => ValueTask.CompletedTask }
                : new ExclusiveGateway { Name = "undo-prefix" };
            scenario.Definition.Elements.Add(target);
            scenario.Definition.Flows.Add(new() { Source = target, Target = sub });
        }
        scenario.Definition.Flows.Single(flow => flow.Source.Name == "compensate-A").Target = target;
        var snapshot = await AdvanceIntoThrowAsync(scenario);
        var thrower = snapshot.Tokens.Single(token => token.StateName == (atEnd ? "throw-end" : "after"));
        var owner = snapshot.Tokens.Single(token => token.WaitingAtName == "undo-sub");
        Assert.NotEqual(thrower.CanonicalName, owner.CanonicalName);
        Assert.Equal(atEnd ? "Completed" : "Active", thrower.State);
        var child = snapshot.Tokens.Single(token => token.StateName == "undo-task");
        snapshot = await scenario.Engine.AdvanceAsync(scenario.Definition, snapshot.Process, snapshot.Tokens, child.CanonicalName, CancellationToken.None);
        Assert.Equal("Completed", owner.State);
        Assert.Equal(atEnd ? "throw-end" : "after", thrower.StateName);
        Assert.Equal(atEnd ? "Completed" : "Running", snapshot.Process.State);
    }

    [Trait("Layer", "Unit")]
    [Theory]
    [InlineData("gateway")]
    [InlineData("procedure")]
    public async Task Throw_ReachThroughRecursiveRoute_FiresCompensationAndContinues(string route) {
        // The throw is not the advance's immediate outgoing target: recursive target resolution
        // through the interceptor must dispatch it through the same owner as a direct advance.
        var scenario = CompensationScenario(3, "A", false);
        FlowElement interceptor = route == "procedure"
            ? new ProcedureTask { Name = "throw-prefix", Body = (_, _) => ValueTask.CompletedTask }
            : new ExclusiveGateway { Name = "throw-prefix" };
        scenario.Definition.Elements.Add(interceptor);
        var intoThrow = scenario.Definition.Flows.Single(flow => flow.Source.Name == "C");
        scenario.Definition.Flows.Remove(intoThrow);
        scenario.Definition.Flows.Add(new() { Source = intoThrow.Source, Target = interceptor });
        scenario.Definition.Flows.Add(new() { Source = interceptor, Target = intoThrow.Target });

        var snapshot = await AdvanceIntoThrowAsync(scenario);

        var transition = Assert.Single(snapshot.Transitions, t => t.Kind == TransitionKind.Compensate);
        Assert.Equal("A", transition.Previous);
        Assert.Equal("undo-A", transition.Posterior);
        Assert.Equal("after", Assert.Single(snapshot.Tokens).StateName);
        Assert.Equal("Active", snapshot.Tokens.Single().State);
        Assert.Contains(snapshot.Transitions, t => t is { Kind: TransitionKind.Move, Previous: "throw", Posterior: "after" });
    }

    private static async Task<ProcessSnapshot> AdvanceIntoThrowAsync(CompensationEngineScenario scenario) {
        var beforeThrow = await AdvanceUntilBeforeThrowAsync(scenario);
        return await scenario.Engine.AdvanceAsync(
            scenario.Definition,
            beforeThrow.Process,
            beforeThrow.Tokens,
            beforeThrow.Tokens.Single().CanonicalName,
            CancellationToken.None);
    }

    private static async Task<ProcessSnapshot> AdvanceUntilBeforeThrowAsync(CompensationEngineScenario scenario) {
        var snapshot = await scenario.Engine.StartAsync(scenario.Definition, scenario.Process, CancellationToken.None);
        for (var i = 1; i < scenario.Activities.Count; i++) {
            snapshot = await scenario.Engine.AdvanceAsync(
                scenario.Definition,
                snapshot.Process,
                snapshot.Tokens,
                snapshot.Tokens.Single().CanonicalName,
                CancellationToken.None);
        }

        Assert.Equal(scenario.Activities.Last().Name, snapshot.Tokens.Single().StateName);
        return snapshot;
    }

    private static CompensationEngineScenario CompensationScenario(int activityCount, string? targeted, bool throwAtEnd) {
        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var activities = Enumerable.Range(0, activityCount)
                                   .Select(i => new NoneTask { Name = ((char)('A' + i)).ToString() })
                                   .Cast<Activity>()
                                   .ToList();
        var throwDefinition = new CompensationDefinition { Name = "Compensate", Activity = targeted is null ? null : activities.Single(a => a.Name == targeted) };
        var throwEvent = new FlowEvent {
            Name         = throwAtEnd ? "throw-end" : "throw",
            Position   = throwAtEnd ? EventPosition.End : EventPosition.IntermediateThrow,
            Definition = throwDefinition,
        };
        var after = new NoneTask { Name = "after" };
        var end   = new FlowEvent { Name = "end", Position = EventPosition.End };
        var definition = new ProcessDefinition { Name = $"compensation-throw-{activityCount}-{targeted ?? "global"}-{throwAtEnd}" };
        definition.Elements.Add(start);
        foreach (var activity in activities) definition.Elements.Add(activity);
        definition.Elements.Add(throwEvent);
        if (!throwAtEnd) {
            definition.Elements.Add(after);
            definition.Elements.Add(end);
        }

        definition.Flows.Add(new() { Source = start, Target = activities[0] });
        for (var i = 0; i < activities.Count - 1; i++) {
            definition.Flows.Add(new() { Source = activities[i], Target = activities[i + 1] });
        }
        definition.Flows.Add(new() { Source = activities[^1], Target = throwEvent });
        if (!throwAtEnd) {
            definition.Flows.Add(new() { Source = throwEvent, Target = after });
            definition.Flows.Add(new() { Source = after, Target = end });
        }

        foreach (var activity in activities) {
            var boundary = new FlowEvent {
                Name         = $"compensate-{activity.Name}",
                Position   = EventPosition.Boundary,
                AttachedTo = activity,
                Definition = new CompensationDefinition { Name = $"Compensate{activity.Name}", Activity = activity },
            };
            var target = new NoneTask { Name = $"undo-{activity.Name}" };
            definition.Elements.Add(boundary);
            definition.Elements.Add(target);
            definition.Flows.Add(new() { Source = boundary, Target = target });
        }

        var process = new SchemataProcess {
            Name           = "p1",
            CanonicalName  = "processes/p1",
            DefinitionName = definition.Name,
        };
        return new(new(), definition, process, activities);
    }

    private sealed record CompensationEngineScenario(
        BpmnEngine          Engine,
        ProcessDefinition  Definition,
        SchemataProcess    Process,
        IReadOnlyList<Activity> Activities);

}
