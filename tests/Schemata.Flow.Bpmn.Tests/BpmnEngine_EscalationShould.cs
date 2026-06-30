using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Xunit;

namespace Schemata.Flow.Bpmn.Tests;

public class BpmnEngine_EscalationShould
{
    [Fact]
    public async Task Throw_IntermediateThrowEscalationWithMatchingBoundaryOnHost_FiresBoundary() {
        var scenario = ThreeLevelScenario(ThrowEscalation("order", "OrderEscalation"), catches => {
            catches.A = BoundaryCatch("a-boundary", "a-boundary", catches.AHost, CatchEscalation("order", "OrderEscalation"), false, "a-handler", "a-handler");
        });
        var snapshot = await AdvanceIntoThrowAsync(scenario);

        Assert.Equal("after-throw", ThrowToken(snapshot).StateName);
        Assert.Single(snapshot.Tokens, t => t.StateName == "a-handler");
        Assert.Contains(snapshot.Transitions, t => t is { Kind: TransitionKind.Spawn, Previous: "a-boundary", Posterior: "a-handler" });
        Assert.Contains(snapshot.Transitions, t => t is { Kind: TransitionKind.Move, Previous: "throw", Posterior: "after-throw" });
    }

    [Fact]
    public async Task Throw_IntermediateThrowEscalationWithMatchingEventSubProcessAtScope_FiresEventSubProcess() {
        var scenario = ThreeLevelScenario(ThrowEscalation("order", "OrderEscalation"), catches => {
            catches.CEventSubProcess = EventSubProcess("c-event-sub", "CEventSub", CatchEscalation("order", "OrderEscalation"), false, "c-event-handler", "CEventHandler");
        });
        var snapshot = await AdvanceIntoThrowAsync(scenario);

        Assert.Equal("after-throw", ThrowToken(snapshot).StateName);
        Assert.Single(snapshot.Tokens, t => t is { ScopeName: "c-event-sub", StateName: "c-event-handler" });
        Assert.Contains(snapshot.Transitions, t => t is { Kind: TransitionKind.Spawn, Previous: "c-event-sub", Posterior: "c-event-handler" });
        Assert.DoesNotContain(snapshot.Transitions, t => t.Kind == TransitionKind.Cancel);
    }

    [Fact]
    public async Task Throw_IntermediateThrowEscalationThreeLevelsDeep_BubblesToOutermostCatch() {
        var scenario = ThreeLevelScenario(ThrowEscalation("outer", "OuterEscalation"), catches => {
            catches.A = BoundaryCatch("a-boundary", "a-boundary", catches.AHost, CatchEscalation("outer", "OuterEscalation"), false, "a-handler", "a-handler");
        });
        var snapshot = await AdvanceIntoThrowAsync(scenario);

        Assert.Equal("after-throw", ThrowToken(snapshot).StateName);
        Assert.Single(snapshot.Tokens, t => t.StateName == "a-handler");
        Assert.DoesNotContain(snapshot.Tokens, t => t.StateName == "b-handler");
        Assert.DoesNotContain(snapshot.Tokens, t => t.StateName == "c-handler");
        Assert.Contains(snapshot.Transitions, t => t is { Kind: TransitionKind.Spawn, Previous: "a-boundary" });
    }

    [Fact]
    public async Task Throw_IntermediateThrowEscalationThreeLevelsDeep_InnerMostCatchWins() {
        var thrown = ThrowEscalation("shared", "SharedEscalation");
        var scenario = ThreeLevelScenario(thrown, catches => {
            catches.A = BoundaryCatch("a-boundary", "a-boundary", catches.AHost, CatchEscalation("shared", "SharedEscalation"), false, "a-handler", "a-handler");
            catches.B = BoundaryCatch("b-boundary", "b-boundary", catches.BHost, CatchEscalation("shared", "SharedEscalation"), false, "b-handler", "b-handler");
            catches.C = BoundaryCatch("c-boundary", "c-boundary", catches.CHost, CatchEscalation("shared", "SharedEscalation"), false, "c-handler", "c-handler");
        });
        var snapshot = await AdvanceIntoThrowAsync(scenario);

        Assert.Equal("after-throw", ThrowToken(snapshot).StateName);
        Assert.Single(snapshot.Tokens, t => t.StateName == "c-handler");
        Assert.DoesNotContain(snapshot.Tokens, t => t.StateName == "a-handler");
        Assert.DoesNotContain(snapshot.Tokens, t => t.StateName == "b-handler");
        var spawn = Assert.Single(snapshot.Transitions, t => t.Kind == TransitionKind.Spawn);
        Assert.Equal("c-boundary", spawn.Previous);
    }

    [Fact]
    public async Task Throw_IntermediateThrowEscalationWithCodeMismatch_DoesNotFire() {
        var scenario = ThreeLevelScenario(ThrowEscalation("X", "EscalationX"), catches => {
            catches.A = BoundaryCatch("a-boundary", "a-boundary", catches.AHost, CatchEscalation("Y", "EscalationY"), true, "a-handler", "a-handler");
        });
        var before = await AdvanceToCSetupAsync(scenario);
        var snapshot = await AdvanceAsync(scenario, before, ThrowToken(before));

        Assert.Equal(before.Tokens.Count, snapshot.Tokens.Count);
        Assert.Equal("after-throw", ThrowToken(snapshot).StateName);
        Assert.DoesNotContain(snapshot.Transitions, t => t.Kind == TransitionKind.Cancel);
        Assert.DoesNotContain(snapshot.Transitions, t => t.Kind == TransitionKind.Spawn);
    }

    [Fact]
    public async Task Throw_IntermediateThrowEscalationWithNoHandlerAtAnyScope_IsSilentlyIgnored() {
        var scenario = ThreeLevelScenario(ThrowEscalation("lost", "LostEscalation"), _ => { });
        var before = await AdvanceToCSetupAsync(scenario);
        var snapshot = await AdvanceAsync(scenario, before, ThrowToken(before));

        Assert.Equal(before.Tokens.Count, snapshot.Tokens.Count);
        Assert.Equal("after-throw", ThrowToken(snapshot).StateName);
        Assert.Equal("Running", snapshot.Process.State);
        Assert.DoesNotContain(snapshot.Transitions, t => t.Kind == TransitionKind.Cancel);
        Assert.DoesNotContain(snapshot.Transitions, t => t.Kind == TransitionKind.Spawn);
    }

    [Fact]
    public async Task Throw_IntermediateThrowEscalationWithWildcardCatch_FiresOnAnyCode() {
        var scenario = ThreeLevelScenario(ThrowEscalation("anything", "SpecificEscalation"), catches => {
            catches.A = BoundaryCatch("a-boundary", "a-boundary", catches.AHost, CatchEscalation(null, "WildcardEscalation"), false, "a-handler", "a-handler");
        });
        var snapshot = await AdvanceIntoThrowAsync(scenario);

        Assert.Equal("after-throw", ThrowToken(snapshot).StateName);
        Assert.Single(snapshot.Tokens, t => t.StateName == "a-handler");
        Assert.Contains(snapshot.Transitions, t => t is { Kind: TransitionKind.Spawn, Previous: "a-boundary" });
    }

    [Fact]
    public async Task Throw_EndEventEscalation_FiresCatchAndConsumesToken() {
        var scenario = ThreeLevelScenario(ThrowEscalation("end", "EndEscalation"), catches => {
            catches.A = BoundaryCatch("a-boundary", "a-boundary", catches.AHost, CatchEscalation("end", "EndEscalation"), false, "a-handler", "a-handler");
        }, true);
        var snapshot = await AdvanceIntoThrowAsync(scenario);

        var throwToken = ThrowToken(snapshot);
        Assert.Equal("throw-end", throwToken.StateName);
        Assert.Equal("Completed", throwToken.State);
        Assert.Single(snapshot.Tokens, t => t.StateName == "a-handler");
        Assert.Contains(snapshot.Transitions, t => t is { Kind: TransitionKind.Spawn, Previous: "a-boundary" });
        Assert.Contains(snapshot.Transitions, t => t is { Kind: TransitionKind.Move, Previous: "c-setup", Posterior: "throw-end" });
    }

    [Fact]
    public async Task Throw_InterruptingEscalationBoundaryOnOuterActivity_CancelsOuterActivity() {
        var scenario = ThreeLevelScenario(ThrowEscalation("boom", "Boom"), catches => {
            catches.A = BoundaryCatch("a-boundary", "a-boundary", catches.AHost, CatchEscalation("boom", "Boom"), true, "a-handler", "a-handler");
        });
        var before = await AdvanceToCSetupAsync(scenario);
        var snapshot = await AdvanceAsync(scenario, before, ThrowToken(before));

        foreach (var token in before.Tokens.Where(t => IsInOuterActivity(t))) {
            Assert.Equal("Cancelled", snapshot.Tokens.Single(t => t.CanonicalName == token.CanonicalName).State);
        }
        Assert.Single(snapshot.Tokens, t => t.StateName == "a-handler");
        Assert.Equal(before.Tokens.Count(IsInOuterActivity), snapshot.Transitions.Count(t => t.Kind == TransitionKind.Cancel));
        Assert.Contains(snapshot.Transitions, t => t is { Kind: TransitionKind.Spawn, Previous: "a-boundary", Posterior: "a-handler" });
    }

    [Fact]
    public async Task Throw_NonInterruptingEscalationBoundaryOnOuterActivity_SpawnsAlongsideAndKeepsHost() {
        var scenario = ThreeLevelScenario(ThrowEscalation("side", "SideEscalation"), catches => {
            catches.A = BoundaryCatch("a-boundary", "a-boundary", catches.AHost, CatchEscalation("side", "SideEscalation"), false, "a-handler", "a-handler");
        });
        var before = await AdvanceToCSetupAsync(scenario);
        var snapshot = await AdvanceAsync(scenario, before, ThrowToken(before));

        foreach (var token in before.Tokens.Where(t => IsInOuterActivity(t))) {
            Assert.NotEqual("Cancelled", snapshot.Tokens.Single(t => t.CanonicalName == token.CanonicalName).State);
        }
        Assert.Equal("after-throw", ThrowToken(snapshot).StateName);
        Assert.Single(snapshot.Tokens, t => t.StateName == "a-handler");
        var spawn = Assert.Single(snapshot.Transitions, t => t.Kind == TransitionKind.Spawn);
        Assert.Equal("a-boundary", spawn.Previous);
        Assert.DoesNotContain(snapshot.Transitions, t => t.Kind == TransitionKind.Cancel);
    }

    private static async Task<ProcessSnapshot> AdvanceIntoThrowAsync(EscalationScenario scenario) {
        var before = await AdvanceToCSetupAsync(scenario);
        return await AdvanceAsync(scenario, before, ThrowToken(before));
    }

    private static async Task<ProcessSnapshot> AdvanceToCSetupAsync(EscalationScenario scenario) {
        var engine  = new BpmnEngine();
        var process = NewProcess(scenario.Definition.Name);
        var snapshot = await engine.StartAsync(scenario.Definition, process, CancellationToken.None);

        snapshot = await AdvanceAsync(scenario, snapshot, snapshot.Tokens.Single(t => t.StateName == "root-setup"));
        snapshot = await AdvanceAsync(scenario, snapshot, snapshot.Tokens.Single(t => t.StateName == "a-setup"));
        snapshot = await AdvanceAsync(scenario, snapshot, snapshot.Tokens.Single(t => t.StateName == "b-setup"));

        var throwToken = ThrowToken(snapshot);
        Assert.Equal("c-setup", throwToken.StateName);
        return snapshot;
    }

    private static async Task<ProcessSnapshot> AdvanceAsync(EscalationScenario scenario, ProcessSnapshot snapshot, SchemataProcessToken token) {
        var engine = new BpmnEngine();
        return await engine.AdvanceAsync(scenario.Definition, snapshot.Process, snapshot.Tokens, token.CanonicalName, CancellationToken.None);
    }

    private static SchemataProcessToken ThrowToken(ProcessSnapshot snapshot) {
        return snapshot.Tokens.Single(t => t is { ScopeName: "c", Spawner: not null });
    }

    private static bool IsInOuterActivity(SchemataProcessToken token) {
        return token.StateName == "a" || token.ScopeName is "a" or "b" or "c";
    }

    private static EscalationDefinition ThrowEscalation(string? code, string name) {
        return new() { Name = name, EscalationCode = code };
    }

    private static EscalationDefinition CatchEscalation(string? code, string name) {
        return new() { Name = name, EscalationCode = code };
    }

    private static BoundarySpec BoundaryCatch(
        string               boundaryId,
        string               boundaryName,
        Activity             host,
        EscalationDefinition definition,
        bool                 interrupting,
        string               handlerId,
        string               handlerName
    ) {
        return new(boundaryId, boundaryName, host, definition, interrupting, handlerId, handlerName);
    }

    private static EventSubProcess EventSubProcess(
        string               id,
        string               name,
        EscalationDefinition definition,
        bool                 interrupting,
        string               handlerId,
        string               handlerName
    ) {
        var eventSub = new EventSubProcess { Name = id };
        var start = new FlowEvent {
            Name           = $"{id}-start",
            Position     = EventPosition.Start,
            Definition   = definition,
            Interrupting = interrupting,
        };
        var handler = new NoneTask { Name = handlerId };
        var end     = new FlowEvent { Name = $"{id}-end", Position = EventPosition.End };
        eventSub.Children.Add(start);
        eventSub.Children.Add(handler);
        eventSub.Children.Add(end);
        eventSub.ChildFlows.Add(new() { Source = start, Target = handler });
        eventSub.ChildFlows.Add(new() { Source = handler, Target = end });
        return eventSub;
    }

    private static EscalationScenario ThreeLevelScenario(
        EscalationDefinition thrown,
        Action<CatchBuilder> configure,
        bool throwAtEnd = false
    ) {
        var start     = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var rootSetup = new NoneTask { Name = "root-setup" };
        var a         = new EmbeddedSubProcess { Name = "a" };
        var rootEnd   = new FlowEvent { Name = "root-end", Position = EventPosition.End };

        var aStart = new FlowEvent { Name = "a-start", Position = EventPosition.Start };
        var aSetup = new NoneTask { Name = "a-setup" };
        var b      = new EmbeddedSubProcess { Name = "b" };
        var aEnd   = new FlowEvent { Name = "a-end", Position = EventPosition.End };

        var bStart = new FlowEvent { Name = "b-start", Position = EventPosition.Start };
        var bSetup = new NoneTask { Name = "b-setup" };
        var c      = new EmbeddedSubProcess { Name = "c" };
        var bEnd   = new FlowEvent { Name = "b-end", Position = EventPosition.End };

        var cStart    = new FlowEvent { Name = "c-start", Position = EventPosition.Start };
        var cSetup    = new NoneTask { Name = "c-setup" };
        var throwNode = throwAtEnd
            ? new FlowEvent { Name = "throw-end", Position = EventPosition.End, Definition = thrown }
            : new FlowEvent { Name = "throw", Position = EventPosition.IntermediateThrow, Definition = thrown };
        var afterThrow = new NoneTask { Name = "after-throw" };
        var cEnd       = new FlowEvent { Name = "c-end", Position = EventPosition.End };

        c.Children.Add(cStart);
        c.Children.Add(cSetup);
        c.Children.Add(throwNode);
        if (!throwAtEnd) {
            c.Children.Add(afterThrow);
            c.Children.Add(cEnd);
            c.ChildFlows.Add(new() { Source = throwNode, Target = afterThrow });
            c.ChildFlows.Add(new() { Source = afterThrow, Target = cEnd });
        }
        c.ChildFlows.Add(new() { Source = cStart, Target = cSetup });
        c.ChildFlows.Add(new() { Source = cSetup, Target = throwNode });

        b.Children.Add(bStart);
        b.Children.Add(bSetup);
        b.Children.Add(c);
        b.Children.Add(bEnd);
        b.ChildFlows.Add(new() { Source = bStart, Target = bSetup });
        b.ChildFlows.Add(new() { Source = bSetup, Target = c });
        b.ChildFlows.Add(new() { Source = c, Target = bEnd });

        a.Children.Add(aStart);
        a.Children.Add(aSetup);
        a.Children.Add(b);
        a.Children.Add(aEnd);
        a.ChildFlows.Add(new() { Source = aStart, Target = aSetup });
        a.ChildFlows.Add(new() { Source = aSetup, Target = b });
        a.ChildFlows.Add(new() { Source = b, Target = aEnd });

        var builder = new CatchBuilder(a, b, c);
        configure(builder);
        AddNestedCatch(builder.B, a);
        AddNestedCatch(builder.C, b);
        if (builder.CEventSubProcess is not null) {
            c.Children.Add(builder.CEventSubProcess);
        }

        var definition = new ProcessDefinition {
            Name     = $"escalation-{Guid.NewGuid():N}",
            Elements = { start, rootSetup, a, rootEnd },
            Flows = {
                new() { Source = start, Target = rootSetup },
                new() { Source = rootSetup, Target = a },
                new() { Source = a, Target = rootEnd },
            },
        };

        AddRootCatch(builder.A, definition);
        return new(definition);
    }

    private static void AddNestedCatch(BoundarySpec? spec, SubProcess parent) {
        if (spec is null) {
            return;
        }

        var boundary = spec.ToBoundary();
        var handler  = new NoneTask { Name = spec.HandlerId };
        var end      = new FlowEvent { Name = $"{spec.HandlerId}-end", Position = EventPosition.End };
        parent!.Children.Add(boundary);
        parent.Children.Add(handler);
        parent.Children.Add(end);
        parent.ChildFlows.Add(new() { Source = boundary, Target = handler });
        parent.ChildFlows.Add(new() { Source = handler, Target = end });
    }

    private static void AddRootCatch(BoundarySpec? spec, ProcessDefinition definition) {
        if (spec is null) {
            return;
        }

        var boundary = spec.ToBoundary();
        var handler  = new NoneTask { Name = spec.HandlerId };
        var end      = new FlowEvent { Name = $"{spec.HandlerId}-end", Position = EventPosition.End };
        definition.Elements.Add(boundary);
        definition.Elements.Add(handler);
        definition.Elements.Add(end);
        definition.Flows.Add(new() { Source = boundary, Target = handler });
        definition.Flows.Add(new() { Source = handler, Target = end });
    }

    private static SchemataProcess NewProcess(string definitionName) {
        return new() {
            Name           = "p1",
            CanonicalName  = "processes/p1",
            DefinitionName = definitionName,
        };
    }

    private sealed record EscalationScenario(ProcessDefinition Definition);

    private sealed class CatchBuilder(Activity a, Activity b, Activity c)
    {
        public Activity AHost { get; } = a;

        public Activity BHost { get; } = b;

        public Activity CHost { get; } = c;

        public BoundarySpec? A { get; set; }

        public BoundarySpec? B { get; set; }

        public BoundarySpec? C { get; set; }

        public EventSubProcess? CEventSubProcess { get; set; }
    }

    private sealed record BoundarySpec(
        string               BoundaryId,
        string               BoundaryName,
        Activity             Host,
        EscalationDefinition Definition,
        bool                 Interrupting,
        string               HandlerId,
        string               HandlerName)
    {
        public FlowEvent ToBoundary() {
            return new() {
                Name           = BoundaryId,
                Position     = EventPosition.Boundary,
                AttachedTo   = Host,
                Interrupting = Interrupting,
                Definition   = Definition,
            };
        }
    }
}
