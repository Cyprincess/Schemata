using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Xunit;

namespace Schemata.Flow.Bpmn.Tests;

public class BpmnEngineEmbeddedShould
{
    [Fact]
    public async Task EnterSubProcess_SpawnsChildAndParksParent() {
        var definition = SubProcessDefinition();
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();

        var started = await engine.StartAsync(definition, process, CancellationToken.None);
        var entered = await engine.AdvanceAsync(definition, started.Process, started.Tokens, null, CancellationToken.None);

        var parent = entered.Tokens.Single(t => t.StateName == "sub");
        Assert.Equal("Waiting", parent.State);
        Assert.Equal("sub", parent.WaitingAtName);
        Assert.Equal(process.Name, parent.ScopeName);

        var child = entered.Tokens.Single(t => t.StateName == "inner-task");
        Assert.Equal("Active", child.State);
        Assert.Equal("sub", child.ScopeName);
        Assert.Equal(parent.CanonicalName, child.Spawner);
    }

    [Fact]
    public async Task ChildReachesInnerEnd_ResumesParentThroughOutgoing() {
        var definition = SubProcessDefinition();
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();

        var snapshot = await engine.StartAsync(definition, process, CancellationToken.None);
        snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, null, CancellationToken.None);

        var child = snapshot.Tokens.First(t => t.StateName == "inner-task");
        snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, child.CanonicalName, CancellationToken.None);

        var resumed = snapshot.Tokens.First(t => string.Equals(t.ScopeName, process.Name, StringComparison.Ordinal));
        Assert.Contains(resumed.StateName, new[] { "after-sub", "end" });

        snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, resumed.CanonicalName, CancellationToken.None);
        var finalToken = snapshot.Tokens.First(t => string.Equals(t.ScopeName, process.Name, StringComparison.Ordinal));

        Assert.Equal("Completed", finalToken.State);
        Assert.Equal("Completed", snapshot.Process.State);
    }

    [Fact]
    public async Task StartIntoSubProcess_DirectlyAfterStartSpawnsChildAndParks() {
        var definition = SubProcessDirectlyAfterStart();
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();

        var started = await engine.StartAsync(definition, process, CancellationToken.None);

        var parent = started.Tokens.Single(t => t.StateName == "sub");
        Assert.Equal("Waiting", parent.State);

        var child = started.Tokens.Single(t => t.StateName == "inner-task");
        Assert.Equal("Active", child.State);
        Assert.Equal("sub", child.ScopeName);
    }

    [Fact]
    public void BpmnValidator_SubProcessWithoutChildren_Throws() {
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var sub      = new EmbeddedSubProcess { Name = "sub" };
        var endEvent = new FlowEvent { Name = "end", Position = EventPosition.End };
        var definition = new ProcessDefinition {
            Name     = "empty-subprocess",
            Elements = { start, sub, endEvent },
            Flows = {
                new() { Source = start, Target = sub },
                new() { Source = sub, Target = endEvent },
            },
        };

        Assert.Throws<FailedPreconditionException>(() => BpmnValidator.Validate(definition));
    }

    [Fact]
    public void BpmnValidator_SubProcessWithoutOutgoing_Throws() {
        var start      = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var sub        = new EmbeddedSubProcess { Name = "sub" };
        var innerStart = new FlowEvent { Name = "in-start", Position = EventPosition.Start };
        var innerEnd   = new FlowEvent { Name = "in-end", Position = EventPosition.End };
        var endEvent   = new FlowEvent { Name = "end", Position = EventPosition.End };

        sub.Children.Add(innerStart);
        sub.Children.Add(innerEnd);
        sub.ChildFlows.Add(new() { Source = innerStart, Target = innerEnd });

        var definition = new ProcessDefinition {
            Name     = "subprocess-no-outgoing",
            Elements = { start, sub, endEvent },
            Flows    = { new() { Source = start, Target = sub } },
        };

        Assert.Throws<FailedPreconditionException>(() => BpmnValidator.Validate(definition));
    }

    private static ProcessDefinition SubProcessDefinition() {
        var start       = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var setup       = new NoneTask { Name = "setup" };
        var sub         = new EmbeddedSubProcess { Name = "sub" };
        var innerStart  = new FlowEvent { Name = "in-start", Position = EventPosition.Start };
        var innerTask   = new NoneTask { Name = "inner-task" };
        var innerEnd    = new FlowEvent { Name = "in-end", Position = EventPosition.End };
        var afterSub    = new NoneTask { Name = "after-sub" };
        var endEvent    = new FlowEvent { Name = "end", Position = EventPosition.End };

        sub.Children.Add(innerStart);
        sub.Children.Add(innerTask);
        sub.Children.Add(innerEnd);
        sub.ChildFlows.Add(new() { Source = innerStart, Target = innerTask });
        sub.ChildFlows.Add(new() { Source = innerTask, Target = innerEnd });

        return new() {
            Name     = "subprocess",
            Elements = { start, setup, sub, afterSub, endEvent },
            Flows = {
                new() { Source = start, Target = setup },
                new() { Source = setup, Target = sub },
                new() { Source = sub, Target = afterSub },
                new() { Source = afterSub, Target = endEvent },
            },
        };
    }

    private static ProcessDefinition SubProcessDirectlyAfterStart() {
        var start       = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var sub         = new EmbeddedSubProcess { Name = "sub" };
        var innerStart  = new FlowEvent { Name = "in-start", Position = EventPosition.Start };
        var innerTask   = new NoneTask { Name = "inner-task" };
        var innerEnd    = new FlowEvent { Name = "in-end", Position = EventPosition.End };
        var endEvent    = new FlowEvent { Name = "end", Position = EventPosition.End };

        sub.Children.Add(innerStart);
        sub.Children.Add(innerTask);
        sub.Children.Add(innerEnd);
        sub.ChildFlows.Add(new() { Source = innerStart, Target = innerTask });
        sub.ChildFlows.Add(new() { Source = innerTask, Target = innerEnd });

        return new() {
            Name     = "subprocess-after-start",
            Elements = { start, sub, endEvent },
            Flows = {
                new() { Source = start, Target = sub },
                new() { Source = sub, Target = endEvent },
            },
        };
    }

    private static SchemataProcess NewProcess(string definitionName) {
        return new() {
            Name           = "p1",
            CanonicalName  = "processes/p1",
            DefinitionName = definitionName,
        };
    }
}
