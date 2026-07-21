using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Xunit;

namespace Schemata.Flow.Bpmn.Tests;

public class BpmnEngine_CompensationShould
{
    [Fact]
    public async Task Throw_SingleActivityCompensation_FiresHandlerOnce() {
        var scenario = LinearScenario(["a"], ["a"], "a");
        var snapshot = await AdvanceIntoThrowAsync(scenario);

        var transition = Assert.Single(snapshot.Transitions, t => t.Kind == TransitionKind.Compensate);
        Assert.Equal("compensate-a", transition.Event);
        Assert.Equal("a", transition.Previous);
        Assert.Equal("undo-a", transition.Posterior);
    }

    [Fact]
    public async Task Throw_GlobalCompensationOnMixedActivities_OnlyCompensatedActivitiesRun() {
        var scenario = LinearScenario(["a", "b", "c"], ["a", "c"], null);
        var snapshot = await AdvanceIntoThrowAsync(scenario);

        var compensations = snapshot.Transitions.Where(t => t.Kind == TransitionKind.Compensate).ToList();
        Assert.Equal(2, compensations.Count);
        Assert.Equal(["c", "a"], compensations.Select(t => t.Previous));
        Assert.Equal(["undo-c", "undo-a"], compensations.Select(t => t.Posterior));
        Assert.DoesNotContain(compensations, t => t.Previous == "b" || t.Posterior == "undo-b");
    }

    [Fact]
    public async Task Throw_NestedSubProcesses_InnerThrowOnlyFiresInnerScopeHandlers() {
        var scenario = NestedScenario(true);
        var snapshot = await RunToInnerThrowAsync(scenario);

        var compensations = snapshot.Transitions.Where(t => t.Kind == TransitionKind.Compensate).ToList();
        Assert.Equal(2, compensations.Count);
        Assert.Equal(["inner-b", "inner-a"], compensations.Select(t => t.Previous));
        Assert.DoesNotContain(compensations, t => t.Previous == "outer-a" || t.Previous == "outer-b");
    }

    [Fact]
    public async Task Throw_NestedSubProcesses_OuterThrowFiresOnlyOuterScopeHandlers() {
        var scenario = NestedScenario(false);
        var snapshot = await RunToOuterThrowAsync(scenario);

        var compensations = snapshot.Transitions.Where(t => t.Kind == TransitionKind.Compensate).ToList();
        Assert.Equal(2, compensations.Count);
        Assert.Equal(["outer-b", "outer-a"], compensations.Select(t => t.Previous));
        Assert.DoesNotContain(compensations, t => t.Previous == "inner-a" || t.Previous == "inner-b");
    }

    [Fact]
    public async Task Throw_NestedSubProcessesWithInnerCompletedNormally_OuterThrowGlobalDoesNotRunInnerCompensations() {
        var scenario = NestedScenario(false);
        var beforeInnerEnd = await RunToInnerLastActivityAsync(scenario);
        var afterInnerEnd = await AdvanceTokenAsync(scenario, beforeInnerEnd, TokenByState(beforeInnerEnd, "inner-b"));

        var snapshot = await AdvanceTokenAsync(scenario, afterInnerEnd, TokenByState(afterInnerEnd, "outer-b"));
        var compensations = snapshot.Transitions.Where(t => t.Kind == TransitionKind.Compensate).ToList();
        Assert.Equal(["outer-b", "outer-a"], compensations.Select(t => t.Previous));
        Assert.DoesNotContain(compensations, t => t.Previous is { } previous && previous.StartsWith("inner", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Throw_TargetedCompensationAmongFiveActivities_FiresOnlyTargetedHandler() {
        var scenario = LinearScenario(["a", "b", "c", "D", "E"], ["a", "b", "c", "D", "E"], "c");
        var snapshot = await AdvanceIntoThrowAsync(scenario);

        var transition = Assert.Single(snapshot.Transitions, t => t.Kind == TransitionKind.Compensate);
        Assert.Equal("c", transition.Previous);
        Assert.Equal("undo-c", transition.Posterior);
        Assert.DoesNotContain(snapshot.Transitions, t => t is { Kind: TransitionKind.Compensate, Previous: "a" or "b" or "D" or "E" });

    }

    private static async Task<ProcessSnapshot> AdvanceIntoThrowAsync(LinearCompensationScenario scenario) {
        var snapshot = await scenario.Engine.StartAsync(scenario.Definition, scenario.Process, CancellationToken.None);
        while (snapshot.Tokens.Single().StateName != scenario.LastActivity.Name) {
            snapshot = await AdvanceTokenAsync(scenario, snapshot, snapshot.Tokens.Single());
        }

        return await AdvanceTokenAsync(scenario, snapshot, snapshot.Tokens.Single());
    }

    private static async Task<ProcessSnapshot> RunToInnerThrowAsync(NestedCompensationScenario scenario) {
        var snapshot = await RunToInnerLastActivityAsync(scenario);
        return await AdvanceTokenAsync(scenario, snapshot, TokenByState(snapshot, "inner-b"));
    }

    private static async Task<ProcessSnapshot> RunToOuterThrowAsync(NestedCompensationScenario scenario) {
        var snapshot = await RunToInnerLastActivityAsync(scenario);
        snapshot = await AdvanceTokenAsync(scenario, snapshot, TokenByState(snapshot, "inner-b"));
        return await AdvanceTokenAsync(scenario, snapshot, TokenByState(snapshot, "outer-b"));
    }

    private static async Task<ProcessSnapshot> RunToInnerLastActivityAsync(EngineScenario scenario) {
        var snapshot = await scenario.Engine.StartAsync(scenario.Definition, scenario.Process, CancellationToken.None);
        snapshot = await AdvanceTokenAsync(scenario, snapshot, TokenByState(snapshot, "outer-a"));
        snapshot = await AdvanceTokenAsync(scenario, snapshot, TokenByState(snapshot, "outer-gap"));
        snapshot = await AdvanceTokenAsync(scenario, snapshot, TokenByState(snapshot, "inner-a"));
        return snapshot;
    }

    private static async Task<ProcessSnapshot> AdvanceTokenAsync(EngineScenario scenario, ProcessSnapshot snapshot, SchemataProcessToken token) {
        return await scenario.Engine.AdvanceAsync(
            scenario.Definition,
            snapshot.Process,
            snapshot.Tokens,
            token.CanonicalName,
            CancellationToken.None);
    }

    private static SchemataProcessToken TokenByState(ProcessSnapshot afterInnerEnd, string stateId) {
        return afterInnerEnd.Tokens.Single(t => t.StateName == stateId);
    }

    private static string ParentCanonicalForScope(ProcessSnapshot snapshot, string scopeId) {
        return snapshot.Tokens.Single(t => t.StateName == scopeId
                                       && t.WaitingAtName == scopeId
                                       && t.State == "Waiting").CanonicalName!;
    }

    private static LinearCompensationScenario LinearScenario(string[] names, string[] compensated, string? targeted) {
        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var activities = names.Select(name => new NoneTask { Name = name.ToLowerInvariant() })
                              .Cast<Activity>()
                              .ToList();
        var targetActivity = targeted is null ? null : activities.Single(a => a.Name == targeted);
        var throwEvent = new FlowEvent {
            Name         = "throw",
            Position   = EventPosition.IntermediateThrow,
            Definition = new CompensationDefinition { Name = "Compensate", Activity = targetActivity },
        };
        var after = new NoneTask { Name = "after" };
        var end   = new FlowEvent { Name = "end", Position = EventPosition.End };
        var definition = new ProcessDefinition { Name = $"compensation-{Guid.NewGuid():N}" };
        definition.Elements.Add(start);
        foreach (var activity in activities) definition.Elements.Add(activity);
        definition.Elements.Add(throwEvent);
        definition.Elements.Add(after);
        definition.Elements.Add(end);

        definition.Flows.Add(new() { Source = start, Target = activities[0] });
        for (var i = 0; i < activities.Count - 1; i++) {
            definition.Flows.Add(new() { Source = activities[i], Target = activities[i + 1] });
        }
        definition.Flows.Add(new() { Source = activities[^1], Target = throwEvent });
        definition.Flows.Add(new() { Source = throwEvent, Target = after });
        definition.Flows.Add(new() { Source = after, Target = end });

        foreach (var activity in activities.Where(a => compensated.Contains(a.Name, StringComparer.Ordinal))) {
            AddCompensation(definition.Elements, definition.Flows, activity, activity.Name);
        }

        return new(new(), definition, NewProcess(definition.Name), activities[^1]);
    }

    private static NestedCompensationScenario NestedScenario(bool throwInInner) {
        var start   = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var outer   = new EmbeddedSubProcess { Name = "outer" };
        var rootEnd = new FlowEvent { Name = "root-end", Position = EventPosition.End };

        var outerStart = new FlowEvent { Name = "outer-start", Position = EventPosition.Start };
        var outerA     = new NoneTask { Name = "outer-a" };
        var outerGap   = new NoneTask { Name = "outer-gap" };
        var inner      = new EmbeddedSubProcess { Name = "inner" };
        var outerB     = new NoneTask { Name = "outer-b" };
        var outerThrow = new FlowEvent {
            Name         = "outer-throw",
            Position   = EventPosition.IntermediateThrow,
            Definition = new CompensationDefinition { Name = "OuterCompensate" },
        };
        var outerAfter = new NoneTask { Name = "outer-after" };
        var outerEnd   = new FlowEvent { Name = "outer-end", Position = EventPosition.End };

        var innerStart = new FlowEvent { Name = "inner-start", Position = EventPosition.Start };
        var innerA     = new NoneTask { Name = "inner-a" };
        var innerB     = new NoneTask { Name = "inner-b" };
        var innerEnd = throwInInner
            ? new FlowEvent {
                Name         = "inner-throw",
                Position   = EventPosition.IntermediateThrow,
                Definition = new CompensationDefinition { Name = "InnerCompensate" },
            }
            : new FlowEvent { Name = "inner-end", Position = EventPosition.End };
        var innerAfter = new NoneTask { Name = "inner-after" };
        var innerDone  = new FlowEvent { Name = "inner-done", Position = EventPosition.End };

        inner.Children.Add(innerStart);
        inner.Children.Add(innerA);
        inner.Children.Add(innerB);
        inner.Children.Add(innerEnd);
        if (throwInInner) {
            inner.Children.Add(innerAfter);
            inner.Children.Add(innerDone);
            inner.ChildFlows.Add(new() { Source = innerEnd, Target = innerAfter });
            inner.ChildFlows.Add(new() { Source = innerAfter, Target = innerDone });
        }
        inner.ChildFlows.Add(new() { Source = innerStart, Target = innerA });
        inner.ChildFlows.Add(new() { Source = innerA, Target = innerB });
        inner.ChildFlows.Add(new() { Source = innerB, Target = innerEnd });
        AddCompensation(inner.Children, inner.ChildFlows, innerA, "inner-a");
        AddCompensation(inner.Children, inner.ChildFlows, innerB, "inner-b");

        outer.Children.Add(outerStart);
        outer.Children.Add(outerA);
        outer.Children.Add(outerGap);
        outer.Children.Add(inner);
        if (!throwInInner) {
            outer.Children.Add(outerB);
            outer.Children.Add(outerThrow);
            outer.Children.Add(outerAfter);
        }
        outer.Children.Add(outerEnd);
        outer.ChildFlows.Add(new() { Source = outerStart, Target = outerA });
        outer.ChildFlows.Add(new() { Source = outerA, Target = outerGap });
        outer.ChildFlows.Add(new() { Source = outerGap, Target = inner });
        if (throwInInner) {
            outer.ChildFlows.Add(new() { Source = inner, Target = outerEnd });
        } else {
            outer.ChildFlows.Add(new() { Source = inner, Target = outerB });
            outer.ChildFlows.Add(new() { Source = outerB, Target = outerThrow });
            outer.ChildFlows.Add(new() { Source = outerThrow, Target = outerAfter });
            outer.ChildFlows.Add(new() { Source = outerAfter, Target = outerEnd });
            AddCompensation(outer.Children, outer.ChildFlows, outerB, "outer-b");
        }
        AddCompensation(outer.Children, outer.ChildFlows, outerA, "outer-a");

        var definition = new ProcessDefinition {
            Name     = $"nested-compensation-{Guid.NewGuid():N}",
            Elements = { start, outer, rootEnd },
            Flows = {
                new() { Source = start, Target = outer },
                new() { Source = outer, Target = rootEnd },
            },
        };

        return new(new(), definition, NewProcess(definition.Name));
    }

    private static void AddCompensation(ICollection<FlowElement> elements, ICollection<SequenceFlow> flows, Activity activity, string suffix) {
        var boundary = new FlowEvent {
            Name         = $"compensate-{suffix.ToLowerInvariant()}",
            Position   = EventPosition.Boundary,
            AttachedTo = activity,
            Definition = new CompensationDefinition { Name = $"compensate-{suffix.ToLowerInvariant()}", Activity = activity },
        };
        var target = new NoneTask { Name = $"undo-{suffix.ToLowerInvariant()}" };
        elements.Add(boundary);
        elements.Add(target);
        flows.Add(new() { Source = boundary, Target = target });
    }

    private static SchemataProcess NewProcess(string definitionName) {
        return new() {
            Name           = "p1",
            CanonicalName  = "processes/p1",
            DefinitionName = definitionName,
        };
    }

    private abstract record EngineScenario(BpmnEngine Engine, ProcessDefinition Definition, SchemataProcess Process);

    private sealed record LinearCompensationScenario(
        BpmnEngine         Engine,
        ProcessDefinition Definition,
        SchemataProcess   Process,
        Activity          LastActivity) : EngineScenario(Engine, Definition, Process);

    private sealed record NestedCompensationScenario(
        BpmnEngine         Engine,
        ProcessDefinition Definition,
        SchemataProcess   Process) : EngineScenario(Engine, Definition, Process);

}
