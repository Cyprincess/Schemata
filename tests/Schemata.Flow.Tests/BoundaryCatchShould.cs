using System;
using System.Linq;
using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Models;
using Xunit;

namespace Schemata.Flow.Tests;

public class BoundaryCatchShould
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Keep_Enter_Leave_And_Boundary_In_Activity_Scope(bool nested) {
        var definition = new ProcessDefinition { Name = "scoped" };
        var host = new NoneTask { Name = "host" };
        var end = new FlowEvent { Name = "end", Position = EventPosition.End };
        var sub = new EmbeddedSubProcess { Name = "sub", Children = { host, end } };
        if (nested) {
            var outer = new EmbeddedSubProcess { Name = "outer", Children = { sub } };
            definition.Elements.Add(outer);
        } else {
            definition.Elements.Add(host);
            definition.Elements.Add(end);
        }

        definition.During(host)
                  .OnEnter((_, _) => default)
                  .OnLeave((_, _) => default)
                  .Go(end)
                  .OnError<InvalidOperationException>().Go(end);

        var elements = nested ? sub.Children : definition.Elements;
        var flows = nested ? sub.ChildFlows : definition.Flows;
        var enter = Assert.Single(elements.OfType<ProcedureTask>(), t => t.Name == "Enter_host");
        var leave = Assert.Single(elements.OfType<ProcedureTask>(), t => t.Name == "Leave_host");
        var boundary = Assert.Single(elements.OfType<FlowEvent>(), e => e.Position == EventPosition.Boundary);
        Assert.Same(host, boundary.AttachedTo);
        Assert.Contains(flows, f => f.Source == enter && f.Target == host);
        Assert.Contains(flows, f => f.Source == host && f.Target == leave);
        Assert.Contains(flows, f => f.Source == leave && f.Target == end);
        Assert.Contains(flows, f => f.Source == boundary && f.Target == end);
        if (nested) {
            Assert.Empty(definition.Flows);
            Assert.DoesNotContain(enter, definition.Elements);
            Assert.DoesNotContain(leave, definition.Elements);
            Assert.DoesNotContain(boundary, definition.Elements);
        }
    }

    [Fact]
    public void OnEscalationWithoutExplicitChoice_DefaultsToNonInterruptingOnlyForEscalation() {
        var escalation = new EscalationDefinition { Name = "DefaultEscalation", EscalationCode = "default" };
        var error      = new ErrorDefinition { Name = "DefaultError", ExceptionType = typeof(InvalidOperationException) };
        var definition = new ProcessDefinition { Name = "dsl-default" };
        var host       = new NoneTask { Name = "Host" };
        var escalationHandler = new NoneTask { Name = "EscalationHandler" };
        var errorHandler      = new NoneTask { Name = "ErrorHandler" };

        definition.Start().Go(host);
        definition.During(host).OnEscalation(escalation).Go(escalationHandler);
        definition.During(host).OnError(error).Go(errorHandler);

        var escalationBoundary = Assert.Single(definition.Elements.OfType<FlowEvent>(), e => e.Definition == escalation);
        var errorBoundary      = Assert.Single(definition.Elements.OfType<FlowEvent>(), e => e.Definition == error);

        Assert.False(escalationBoundary.Interrupting);
        Assert.True(errorBoundary.Interrupting);
    }
}
