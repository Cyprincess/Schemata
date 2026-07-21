using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Flow.Bpmn.Runtime.Boundary;
using Schemata.Flow.Bpmn.Runtime.Compensation;
using Schemata.Flow.Skeleton.Models;
using Xunit;

namespace Schemata.Flow.Bpmn.Tests;

public class CompensationBoundaryHandlerShould
{
    [Fact]
    public void FindCompensationBoundaries_HostWithNoCompensationBoundary_ReturnsEmpty() {
        var scenario = BoundaryScenario(0, true);

        var boundaries = CompensationBoundaryHandler.FindCompensationBoundaries(scenario.Definition, scenario.Host).ToList();

        Assert.Empty(boundaries);
    }

    [Fact]
    public void FindCompensationBoundaries_HostWithOneCompensationBoundary_ReturnsOne() {
        var scenario = BoundaryScenario(1, true);

        var boundaries = CompensationBoundaryHandler.FindCompensationBoundaries(scenario.Definition, scenario.Host).ToList();

        var boundary = Assert.Single(boundaries);
        Assert.Equal("boundary-1", boundary.Name);
    }

    [Fact]
    public void FindCompensationBoundaries_HostWithTwoCompensationBoundaries_ReturnsTwo() {
        var scenario = BoundaryScenario(2, true);

        var boundaries = CompensationBoundaryHandler.FindCompensationBoundaries(scenario.Definition, scenario.Host).ToList();

        Assert.Equal(2, boundaries.Count);
        Assert.Collection(
            boundaries,
            first => Assert.Equal("boundary-1", first.Name),
            second => Assert.Equal("boundary-2", second.Name));
    }

    [Fact]
    public void Build_BoundaryWithOutgoingFlow_CreatesHandlerWithCorrectTarget() {
        var scenario = BoundaryScenario(1, false);
        var boundary = CompensationBoundaryHandler.FindCompensationBoundaries(scenario.Definition, scenario.Host).Single();

        var handler = CompensationBoundaryHandler.Build(scenario.Definition, scenario.Host, boundary);

        Assert.NotNull(handler);
        Assert.Same(scenario.Host, handler.Activity);
        Assert.Same(scenario.CompensationTargets[0], handler.CompensationTarget);
    }

    [Fact]
    public void Build_BoundaryWithoutOutgoingFlow_ReturnsNull() {
        var host = new NoneTask { Name = "host" };
        var boundary = new FlowEvent {
            Name         = "boundary",
            Position   = EventPosition.Boundary,
            AttachedTo = host,
            Definition = new CompensationDefinition { Name = "boundary" },
        };
        var definition = new ProcessDefinition {
            Name     = "missing-outgoing",
            Elements = { host, boundary },
        };

        var handler = CompensationBoundaryHandler.Build(definition, host, boundary);

        Assert.Null(handler);
    }

    [Fact]
    public void RegisterAll_TwoBoundariesPushedInDefinitionOrder_StackSnapshotMatches() {
        var scenario = BoundaryScenario(2, true);
        var stack    = new CompensationStack();

        CompensationBoundaryHandler.RegisterAll(scenario.Definition, scenario.Host, stack);

        var snapshot = stack.Snapshot().Cast<BoundaryCompensationHandler>().ToList();
        Assert.Equal(2, snapshot.Count);
        Assert.Equal("boundary-1", snapshot[0].EventName);
        Assert.Same(scenario.CompensationTargets[0], snapshot[0].CompensationTarget);
        Assert.Equal("boundary-2", snapshot[1].EventName);
        Assert.Same(scenario.CompensationTargets[1], snapshot[1].CompensationTarget);
    }

    [Fact]
    public async Task InvokeAsync_BoundaryHandlerWithStubExecutor_ForwardsCompensationInvocation() {
        var scenario = BoundaryScenario(1, false);
        var boundary = CompensationBoundaryHandler.FindCompensationBoundaries(scenario.Definition, scenario.Host).Single();
        Activity? invokedActivity = null;
        FlowElement? invokedTarget = null;
        string? invokedEventName = null;
        CompensationInvocationContext? invokedContext = null;
        var executor = new Mock<ICompensationExecutor>();
        executor.Setup(e => e.ExecuteAsync(
                    It.IsAny<Activity>(),
                    It.IsAny<FlowElement>(),
                    It.IsAny<string>(),
                    It.IsAny<CompensationInvocationContext>(),
                    It.IsAny<CancellationToken>()))
                .Callback((Activity activity, FlowElement target, string eventName, CompensationInvocationContext invocation, CancellationToken _) => {
                    invokedActivity  = activity;
                    invokedTarget    = target;
                    invokedEventName = eventName;
                    invokedContext   = invocation;
                })
                .Returns(ValueTask.CompletedTask);
        var handler  = new BoundaryCompensationHandler(scenario.Host, scenario.CompensationTargets[0], boundary.Name, executor.Object);
        var context  = NewContext(scenario.Definition);

        await handler.InvokeAsync(context);

        Assert.Same(context, invokedContext);
        Assert.Same(scenario.Host, invokedActivity);
        Assert.Same(scenario.CompensationTargets[0], invokedTarget);
        Assert.Equal(boundary.Name, invokedEventName);
    }

    [Fact]
    public async Task InvokeAsync_ExecutorThrows_PropagatesException() {
        var scenario = BoundaryScenario(1, false);
        var failure  = new InvalidOperationException("boom");
        var executor = new Mock<ICompensationExecutor>();
        executor.Setup(e => e.ExecuteAsync(
                    It.IsAny<Activity>(),
                    It.IsAny<FlowElement>(),
                    It.IsAny<string>(),
                    It.IsAny<CompensationInvocationContext>(),
                    It.IsAny<CancellationToken>()))
                .Throws(failure);
        var handler  = new BoundaryCompensationHandler(
            scenario.Host,
            scenario.CompensationTargets[0],
            "boundary-1",
            executor.Object);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await handler.InvokeAsync(NewContext(scenario.Definition)));

        Assert.Same(failure, thrown);
    }

    private static BoundaryTestScenario BoundaryScenario(int compensationBoundaries, bool includeSignalBoundary) {
        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var host  = new NoneTask { Name = "host" };
        var end   = new FlowEvent { Name = "end", Position = EventPosition.End };
        var definition = new ProcessDefinition {
            Name     = "compensation-boundary",
            Elements = { start, host, end },
            Flows = {
                new() { Source = start, Target = host },
                new() { Source = host, Target = end },
            },
        };
        var targets = new List<Activity>();

        if (includeSignalBoundary) {
            var signalBoundary = new FlowEvent {
                Name         = "signal-boundary",
                Position   = EventPosition.Boundary,
                AttachedTo = host,
                Definition = new Signal { Name = "signal-boundary" },
            };
            var signalTarget = new NoneTask { Name = "signal-handler" };
            definition.Elements.Add(signalBoundary);
            definition.Elements.Add(signalTarget);
            definition.Flows.Add(new() { Source = signalBoundary, Target = signalTarget });
        }

        for (var i = 1; i <= compensationBoundaries; i++) {
            var boundary = new FlowEvent {
                Name         = $"boundary-{i}",
                Position   = EventPosition.Boundary,
                AttachedTo = host,
                Definition = new CompensationDefinition { Name = $"boundary-{i}", Activity = host },
            };
            var target = new NoneTask { Name = $"compensate-{i}" };
            definition.Elements.Add(boundary);
            definition.Elements.Add(target);
            definition.Flows.Add(new() { Source = boundary, Target = target });
            targets.Add(target);
        }

        return new(definition, host, targets);
    }

    private static CompensationInvocationContext NewContext(ProcessDefinition definition) {
        return new(
            new() { Name = "p1", CanonicalName = "processes/p1", DefinitionName = definition.Name },
            definition,
            new() {
                CanonicalName = "processes/p1/tokens/root",
                ScopeName       = "compensation-boundary",
                StateName       = "host",
                Status        = "Compensating",
            },
            new Dictionary<string, int>());
    }

    private sealed record BoundaryTestScenario(
        ProcessDefinition Definition,
        Activity          Host,
        IReadOnlyList<Activity> CompensationTargets);

}
