using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Bpmn.Tests;

public class BpmnProcedureTaskShould
{
    [Fact]
    public async Task Start_Procedure_Task_Executes_Body_And_Follows_Auto_Flow() {
        var executed = false;
        var definition = Definition(new ProcedureTask {
            Name = "procedure",
            Body = _ => {
                executed = true;
                return ValueTask.CompletedTask;
            },
        });
        var engine = new BpmnEngine();

        var snapshot = await engine.StartAsync(definition, Process(definition), CancellationToken.None);

        Assert.True(executed);
        Assert.Equal("next", Assert.Single(snapshot.Tokens).StateName);
    }

    [Fact]
    public async Task Trigger_Message_Payload_Executes_Typed_Procedure_Body() {
        var payload = 0;
        var start   = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var gateway = new EventBasedGateway { Name = "await" };
        var message = new Message<int> { Name = "submitted" };
        var caught = new FlowEvent {
            Name       = "caught",
            Position   = EventPosition.IntermediateCatch,
            Definition = message,
        };
        var procedure = new ProcedureTask<int> {
            Name = "procedure",
            Body = (_, value) => {
                payload = value;
                return ValueTask.CompletedTask;
            },
        };
        var end = new FlowEvent { Name = "end", Position = EventPosition.End };
        var definition = new ProcessDefinition {
            Name     = "typed-procedure",
            Elements = { start, gateway, caught, procedure, end },
            Flows = {
                new() { Source = start, Target = gateway },
                new() { Source = gateway, Target = caught },
                new() { Source = caught, Target = procedure },
                new() { Source = procedure, Target = end },
            },
        };
        var engine  = new BpmnEngine();
        var started = await engine.StartAsync(definition, Process(definition), CancellationToken.None);

        var snapshot = await engine.TriggerAsync(
            definition,
            started.Process,
            started.Tokens,
            message,
            42,
            started.Tokens[0].CanonicalName,
            CancellationToken.None);

        Assert.Equal(42, payload);
        Assert.Equal("Completed", snapshot.Process.State);
    }

    [Fact]
    public async Task Trigger_Message_Payload_Evaluates_Source_Payload_Condition() {
        var start   = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var gateway = new EventBasedGateway { Name = "await" };
        var message = new Message<int> { Name = "submitted" };
        var caught = new FlowEvent {
            Name       = "caught",
            Position   = EventPosition.IntermediateCatch,
            Definition = message,
        };
        var decision = new ExclusiveGateway { Name = "decision" };
        var approved = new FlowEvent { Name = "approved", Position = EventPosition.End };
        var rejected = new FlowEvent { Name = "rejected", Position = EventPosition.End };
        var definition = new ProcessDefinition {
            Name     = "payload-condition",
            Elements = { start, gateway, caught, decision, approved, rejected },
            Flows = {
                new() { Source = start, Target = gateway },
                new() { Source = gateway, Target = caught },
                new() { Source = caught, Target = decision },
                new() {
                    Source    = decision,
                    Target    = approved,
                    Condition = new SourcePayloadConditionExpression<Order, int>("order", (order, payload) => order.State == "paid" && payload == 42),
                },
                new() { Source = decision, Target = rejected, IsDefault = true },
            },
        };
        var process = Process(definition);
        var engine  = new BpmnEngine();
        var started = await engine.StartAsync(definition, process, CancellationToken.None);
        var order   = new Order { Name = "o1", CanonicalName = "orders/o1", State = "paid" };
        var token   = Assert.Single(started.Tokens);
        var services = new ServiceCollection()
                      .AddSingleton(Repository(new SchemataProcessSource {
                          Process = process.CanonicalName!,
                          Token = token.CanonicalName,
                          Name = "order",
                          Source = order.CanonicalName!,
                          SourceType = typeof(Order).FullName!,
                      }).Object)
                      .AddSingleton(Repository(order).Object)
                      .BuildServiceProvider();

        var snapshot = await engine.TriggerAsync(
            definition,
            process,
            started.Tokens,
            new FlowExecutionContext(Mock.Of<IUnitOfWork>(), services),
            message,
            42,
            token.CanonicalName,
            CancellationToken.None);

        Assert.Equal("approved", Assert.Single(snapshot.Tokens).StateName);
    }

    private static ProcessDefinition Definition(ProcedureTask procedure) {
        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var next  = new UserTask { Name = "next" };
        var end   = new FlowEvent { Name = "end", Position = EventPosition.End };
        return new() {
            Name     = "procedure",
            Elements = { start, procedure, next, end },
            Flows = {
                new() { Source = start, Target = procedure },
                new() { Source = procedure, Target = next },
                new() { Source = next, Target = end },
            },
        };
    }

    private static SchemataProcess Process(ProcessDefinition definition) {
        return new() {
            Name           = "p1",
            CanonicalName  = "processes/p1",
            DefinitionName = definition.Name,
        };
    }

    private static Mock<IRepository<T>> Repository<T>(params T[] values)
        where T : class {
        var data = values.ToList();
        var repository = new Mock<IRepository<T>>();
        repository.Setup(candidate => candidate.SingleOrDefaultAsync(
                      It.IsAny<Func<IQueryable<T>, IQueryable<T>>>(),
                      It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<T>, IQueryable<T>> query, CancellationToken _) =>
                      new ValueTask<T?>(query(data.AsQueryable()).SingleOrDefault()));
        return repository;
    }

    public sealed class Order : ICanonicalName
    {
        public string? Name { get; set; }

        public string? CanonicalName { get; set; }

        public string? State { get; set; }
    }
}
