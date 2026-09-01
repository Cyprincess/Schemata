using System.Linq;
using System.Threading.Tasks;
using Moq;
using Schemata.Flow.Foundation.Handlers;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Tests;

public class ListProcessDefinitionsHandlerShould
{
    [Fact]
    public async Task ListDefinitions_ProjectsElementGraphWithPerEventTriggers() {
        var message = new Message { Name = "approve" };
        var start   = new StartEvent { Name = "begin" };
        var task    = new NoneTask { Name = "review" };
        var gateway = new EventBasedGateway { Name = "Await_review" };
        var catchEvent = new FlowEvent {
            Name       = "Catch_Await_review_approve",
            Position   = EventPosition.IntermediateCatch,
            Definition = message,
        };
        var end = new EndEvent { Name = "done" };

        var definition = new ProcessDefinition { Name = "orders" };
        definition.Elements.AddRange([start, task, gateway, catchEvent, end]);
        definition.Flows.AddRange([
            new() { Source = start, Target = task },
            new() { Source = task, Target = gateway },
            new() { Source = gateway, Target = catchEvent },
            new() { Source = catchEvent, Target = end },
        ]);
        definition.Messages.Add(message);

        var registration = new ProcessRegistration {
            Name          = "orders",
            Engine        = "StateMachine",
            Definition    = definition,
            Configuration = new() { Name = "orders" },
        };

        var registry = new Mock<IProcessRegistry>();
        registry.Setup(r => r.GetRegisteredProcesses()).Returns(["orders"]);
        registry.Setup(r => r.GetRegistration("orders")).Returns(registration);

        var handler = new DefaultListProcessDefinitionsHandler(registry.Object);

        var info = Assert.Single(await handler.HandleAsync(new()));

        Assert.Equal(5, info.Elements.Count);
        Assert.Equal(new[] { "begin", "review", "Await_review", "Catch_Await_review_approve", "done" },
                     info.Elements.Select(e => e.Name).ToArray());
        Assert.Equal(new[] { "StartEvent", "NoneTask", "EventBasedGateway", "FlowEvent", "EndEvent" },
                     info.Elements.Select(e => e.Kind).ToArray());

        var trigger = info.Elements.Single(e => e.Name == "Catch_Await_review_approve");
        Assert.Equal(EventPosition.IntermediateCatch, trigger.Position);
        Assert.Equal("approve", trigger.Trigger);

        Assert.Null(info.Elements.Single(e => e.Name == "review").Position);
        Assert.Null(info.Elements.Single(e => e.Name == "review").Trigger);

        Assert.Equal(4, info.Flows.Count);
        Assert.Equal([("begin", "review"), ("review", "Await_review"), ("Await_review", "Catch_Await_review_approve"), ("Catch_Await_review_approve", "done")],
                     info.Flows.Select(f => (f.Source, f.Target)).ToArray());
    }

    [Fact]
    public async Task ListDefinitions_LeavesGraphEmptyWhenRegistrationIsMissing() {
        var registry = new Mock<IProcessRegistry>();
        registry.Setup(r => r.GetRegisteredProcesses()).Returns(["orders"]);
        registry.Setup(r => r.GetRegistration("orders")).Returns((ProcessRegistration?)null);

        var handler = new DefaultListProcessDefinitionsHandler(registry.Object);

        var info = Assert.Single(await handler.HandleAsync(new()));
        Assert.Equal("definitions/orders", info.CanonicalName);
        Assert.Empty(info.Elements);
        Assert.Empty(info.Flows);
    }
}
