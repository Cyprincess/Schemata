using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.StateMachine;
using Xunit;

namespace Schemata.Flow.Tests;

public class DefinitionRebuildShould
{
    [Fact]
    public void Generate_Identical_Element_Names_Across_Definition_Rebuilds() {
        var first  = new ApprovalProcess();
        var second = new ApprovalProcess();

        Assert.Equal(
            first.AllElements.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal).ToList(),
            second.AllElements.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal).ToList());

        Assert.Equal(
            first.AllFlows.Select(f => $"{f.Source.Name}->{f.Target.Name}").OrderBy(n => n, StringComparer.Ordinal).ToList(),
            second.AllFlows.Select(f => $"{f.Source.Name}->{f.Target.Name}").OrderBy(n => n, StringComparer.Ordinal).ToList());

        var names = first.AllElements.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("Start", names);
        Assert.Contains("Await_Review", names);
        Assert.Contains("Catch_Await_Review_Approved", names);
        Assert.Contains("Catch_Await_Review_Rejected", names);
        Assert.Contains("End_Ship", names);
        Assert.Contains("End_Refund", names);
    }

    [Fact]
    public async Task Advance_Persisted_Token_Against_Freshly_Rebuilt_Definition() {
        var engine  = new StateMachineEngine();
        var process = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1" };

        var started = await engine.StartAsync(new ApprovalProcess(), process, Context());
        Assert.Equal("Review", started.Tokens[0].StateName);

        var rebuilt  = new ApprovalProcess();
        var advanced = await engine.AdvanceAsync(rebuilt, process, started.Tokens, Context());

        Assert.Equal("Await_Review", advanced.Tokens[0].WaitingAtName);
        Assert.Equal("Waiting", advanced.Tokens[0].State);
    }

    [Fact]
    public async Task Trigger_Waiting_Token_Against_Freshly_Rebuilt_Definition() {
        var engine  = new StateMachineEngine();
        var process = new SchemataProcess { Name = "p2", CanonicalName = "processes/p2" };

        var started = await engine.StartAsync(new ApprovalProcess(), process, Context());
        var waiting = await engine.AdvanceAsync(new ApprovalProcess(), process, started.Tokens, Context());

        var rebuilt   = new ApprovalProcess();
        var triggered = await engine.TriggerAsync(rebuilt, process, waiting.Tokens, Context(), rebuilt.Approved, null);

        Assert.Equal("Ship", triggered.Tokens[0].StateName);
        Assert.Equal("Active", triggered.Tokens[0].State);
    }

    private static FlowExecutionContext Context() {
        return new(Mock.Of<IUnitOfWork>(), new ServiceCollection().BuildServiceProvider());
    }

    private sealed class ApprovalProcess : ProcessDefinition
    {
        public ApprovalProcess() {
            this.Start().Go(Review);
            this.During(Review).Await(
                this.On(Approved).Go(Ship),
                this.On(Rejected).Go(Refund));
            this.During(Ship).End();
            this.During(Refund).End();
        }

        public UserTask Review { get; } = null!;

        public UserTask Ship { get; } = null!;

        public UserTask Refund { get; } = null!;

        public Message Approved { get; } = null!;

        public Message Rejected { get; } = null!;
    }
}
