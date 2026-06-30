using System;
using System.Linq;
using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Models;
using Xunit;

namespace Schemata.Flow.Tests;

public class BoundaryCatchShould
{
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
