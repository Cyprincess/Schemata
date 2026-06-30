using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Bpmn.Runtime.Compensation;
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

    [Fact]
    public async Task Throw_CompensationHandlerThrows_PropagatesError() {
        var scenario = CompensationScenario(1, null, false);
        var beforeThrow = await AdvanceUntilBeforeThrowAsync(scenario);
        var failure = new InvalidOperationException("compensation failed");
        RegisterHandler(scenario.Engine, beforeThrow.Process.CanonicalName!, new ThrowingHandler(failure));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await scenario.Engine.AdvanceAsync(
                scenario.Definition,
                beforeThrow.Process,
                beforeThrow.Tokens,
                beforeThrow.Tokens.Single().CanonicalName,
                CancellationToken.None));

        Assert.Same(failure, thrown);
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

    private static void RegisterHandler(BpmnEngine engine, string scopeOwner, ICompensationHandler handler) {
        var ensure = typeof(BpmnEngine).GetMethod(
            "EnsureCompensationScope",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(ensure);

        var stack = Assert.IsType<CompensationStack>(ensure.Invoke(engine, [scopeOwner]));
        stack.Register(handler);
    }

    private sealed record CompensationEngineScenario(
        BpmnEngine          Engine,
        ProcessDefinition  Definition,
        SchemataProcess    Process,
        IReadOnlyList<Activity> Activities);

    private sealed class ThrowingHandler(Exception failure) : ICompensationHandler
    {
        public Activity Activity { get; } = new NoneTask { Name = "throwing" };

        public FlowElement CompensationTarget { get; } = new NoneTask { Name = "undo-throwing" };

        public ValueTask InvokeAsync(CompensationInvocationContext context, CancellationToken ct = default) {
            throw failure;
        }
    }
}

