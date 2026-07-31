using System.Collections.Generic;
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

public class EventOnEnterShould
{
    [Fact]
    public void Insert_Procedure_Task_Before_End_Event() {
        var definition = new EndEnterProcess([]);

        var enter = definition.Elements.OfType<ProcedureTask>().Single(t => t.Name == "Enter_Finish");

        Assert.Same(enter, definition.Flows.Single(f => f.Target == definition.Finish).Source);
        Assert.Contains(definition.Flows, f => f.Source == definition.Work && f.Target == enter);
    }

    [Fact]
    public void Insert_Procedure_Task_On_Catch_Outgoing() {
        var definition = new CatchEnterProcess([]);

        var catchEvent = definition.Elements.OfType<FlowEvent>()
                                   .Single(e => e.Position == EventPosition.IntermediateCatch);
        var enter = definition.Elements.OfType<ProcedureTask>().Single(t => t.Name.StartsWith("Enter_Catch"));

        Assert.Same(enter, definition.Flows.Single(f => f.Source == catchEvent).Target);
        Assert.Contains(definition.Flows, f => f.Source == enter && f.Target == definition.Done);
    }

    [Fact]
    public async Task Run_End_Event_OnEnter_Before_Completion() {
        var log        = new List<string>();
        var definition = new EndEnterProcess(log);
        var engine     = new StateMachineEngine();
        var process    = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1" };
        var context    = Context();

        var started = await engine.StartAsync(definition, process, context);
        Assert.Empty(log);

        var advanced = await engine.AdvanceAsync(definition, process, started.Tokens, context);

        Assert.Equal(["end"], log);
        Assert.Equal("Completed", advanced.Tokens[0].State);
    }

    [Fact]
    public async Task Run_Catch_OnEnter_When_Catch_Fires_But_Not_On_Query() {
        var log        = new List<string>();
        var definition = new CatchEnterProcess(log);
        var engine     = new StateMachineEngine();
        var process    = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1" };
        var context    = Context();

        var started = await engine.StartAsync(definition, process, context);
        Assert.Empty(log);

        var waiting = await engine.AdvanceAsync(definition, process, started.Tokens, context);

        var targets = await engine.FindTriggerTargetsAsync(definition, process, waiting.Tokens, context, definition.Pay);
        Assert.NotEmpty(targets);
        Assert.Empty(log);

        await engine.TriggerAsync(definition, process, waiting.Tokens, context, definition.Pay, null);
        Assert.Equal(["catch"], log);
    }

    private static FlowExecutionContext Context() {
        return new(Mock.Of<IUnitOfWork>(), new ServiceCollection().BuildServiceProvider());
    }

    #region Nested type: EndEnterProcess

    private sealed class EndEnterProcess : ProcessDefinition
    {
        public EndEnterProcess(List<string> log) {
            this.During(Finish).OnEnter(_ => {
                log.Add("end");
                return ValueTask.CompletedTask;
            });
            this.Start().Go(Work);
            this.During(Work).End(Finish);
        }

        public UserTask Work   { get; } = null!;
        public EndEvent Finish { get; } = null!;
    }

    #endregion

    #region Nested type: CatchEnterProcess

    private sealed class CatchEnterProcess : ProcessDefinition
    {
        public CatchEnterProcess(List<string> log) {
            this.Start().Go(New);
            this.During(New).Await(this.On(Pay).OnEnter(_ => {
                log.Add("catch");
                return ValueTask.CompletedTask;
            }).Go(Done));
            this.During(Done).End();
        }

        public NoneTask New  { get; } = null!;
        public NoneTask Done { get; } = null!;
        public Message  Pay  { get; } = null!;
    }

    #endregion
}
