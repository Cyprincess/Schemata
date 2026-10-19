using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Flow.Foundation;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.StateMachine;
using Xunit;

namespace Schemata.Flow.Tests;

public class StateMachineFoundationShould
{
    [Trait("Layer", "Unit")]
    [Fact]
    public async Task Advance_SingleFalseGuard_DoesNotTraverse() {
        var task = new NoneTask { Name = "task" };
        var end = new FlowEvent { Name = "end", Position = EventPosition.End };
        var definition = new ProcessDefinition { Name = "guard", Elements = { task, end }, Flows = {
            new() { Source = task, Target = end, Condition = new LambdaConditionExpression { Lambda = _ => new(false) } },
        } };
        var token = new SchemataProcessToken { CanonicalName = "processes/p1/tokens/t1", Process = "p1", ScopeName = string.Empty, StateName = "task", State = "Active" };
        using var services = new ServiceCollection().BuildServiceProvider();
        var snapshot = await new StateMachineEngine().AdvanceAsync(definition, new() { Name = "p1", CanonicalName = "processes/p1" }, [token], FlowTestCreation.Context(Mock.Of<IUnitOfWork>(), services));

        Assert.Equal("task", token.StateName);
        Assert.Empty(snapshot.Transitions);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async Task Trigger_Through_Catch_Chained_To_Another_Event_Gateway_Parks_At_The_Gateway() {
        // A catch feeding a second event-based gateway must park on that gateway: selecting one of
        // its outgoing catches up front would consume a branch before its trigger arrived.
        var start  = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var g1     = new EventBasedGateway { Name = "g1" };
        var catchA = new FlowEvent { Name = "catch-a", Position = EventPosition.IntermediateCatch, Definition = new Message { Name = "a-msg" } };
        var g2     = new EventBasedGateway { Name = "g2" };
        var catchB = new FlowEvent { Name = "catch-b", Position = EventPosition.IntermediateCatch, Definition = new Message { Name = "b-msg" } };
        var catchC = new FlowEvent { Name = "catch-c", Position = EventPosition.IntermediateCatch, Definition = new Message { Name = "c-msg" } };
        var endB   = new FlowEvent { Name = "end-b", Position = EventPosition.End };
        var endC   = new FlowEvent { Name = "end-c", Position = EventPosition.End };
        var definition = new ProcessDefinition {
            Name = "chained-gateways",
            Elements = { start, g1, catchA, g2, catchB, catchC, endB, endC },
            Flows = {
                new() { Source = start,   Target = g1 },
                new() { Source = g1,      Target = catchA },
                new() { Source = catchA,  Target = g2 },
                new() { Source = g2,      Target = catchB },
                new() { Source = g2,      Target = catchC },
                new() { Source = catchB,  Target = endB },
                new() { Source = catchC,  Target = endC },
            },
        };
        var process = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1" };
        var engine  = new StateMachineEngine();

        var started = await engine.StartAsync(definition, process, FlowTestCreation.Context(Mock.Of<IUnitOfWork>(), new ServiceCollection().BuildServiceProvider()));

        var first = await engine.TriggerAsync(
            definition, started.Process, started.Tokens,
            FlowTestCreation.Context(Mock.Of<IUnitOfWork>(), new ServiceCollection().BuildServiceProvider()),
            catchA.Definition!, null, null, CancellationToken.None);

        Assert.Equal("g2", first.Tokens[0].StateName);
        Assert.Equal("g2", first.Tokens[0].WaitingAtName);
        Assert.Equal("Waiting", first.Tokens[0].State);

        var second = await engine.TriggerAsync(
            definition, first.Process, first.Tokens,
            FlowTestCreation.Context(Mock.Of<IUnitOfWork>(), new ServiceCollection().BuildServiceProvider()),
            catchC.Definition!, null, null, CancellationToken.None);

        Assert.Equal("end-c", second.Tokens[0].StateName);
        Assert.Equal("Completed", second.Tokens[0].State);
        Assert.Equal("Completed", second.Process.State);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async Task Advance_ExplicitDefault_DoesNotEvaluateItsGuard() {
        var task = new NoneTask { Name = "task" };
        var fallback = new NoneTask { Name = "fallback" };
        var selected = new NoneTask { Name = "selected" };
        var definition = new ProcessDefinition { Name = "default", Elements = { task, fallback, selected }, Flows = {
            new() { Source = task, Target = selected, IsDefault = true, Condition = new LambdaConditionExpression { Lambda = _ => throw new InvalidOperationException("default evaluated") } },
            new() { Source = task, Target = fallback },
        } };
        var token = new SchemataProcessToken { CanonicalName = "processes/p1/tokens/t1", Process = "p1", ScopeName = string.Empty, StateName = "task", State = "Active" };
        using var services = new ServiceCollection().BuildServiceProvider();
        await new StateMachineEngine().AdvanceAsync(definition, new() { Name = "p1", CanonicalName = "processes/p1" }, [token], FlowTestCreation.Context(Mock.Of<IUnitOfWork>(), services));

        Assert.Equal("selected", token.StateName);
    }

    [Fact]
    public async Task Execute_Procedure_Task_Writes_Multiple_Entities_In_Current_Unit_Of_Work() {
        var definition = new InventoryProcess();
        var uow = Mock.Of<IUnitOfWork>();
        var transactions = Repository<Transaction>();
        var stocks = Repository(new Stock { Name = "stock1", CanonicalName = "stocks/stock1", Quantity = 3 });
        var services = new ServiceCollection()
                      .AddSingleton(transactions.Object)
                      .AddSingleton(stocks.Object)
                      .BuildServiceProvider();
        var engine = new StateMachineEngine();
        var process = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1" };

        var snapshot = await engine.StartAsync(definition, process, FlowTestCreation.Context(uow, services));

        Assert.Equal(definition.Reserve.Name, snapshot.Tokens[0].StateName);
        transactions.Verify(r => r.Join(uow), Times.Once);
        stocks.Verify(r => r.Join(uow), Times.Once);
        transactions.Verify(r => r.AddAsync(It.Is<Transaction>(t => t.CanonicalName == "transactions/tx1"), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()), Times.Once);
        stocks.Verify(r => r.UpdateAsync(It.Is<Stock>(s => s.Quantity == 2), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()), Times.Once);
    }

    [Fact]
    public async Task Roll_Back_Unit_Of_Work_When_Flow_Work_Fails() {
        var uow = new Mock<IUnitOfWork>();
        var processes = Repository<SchemataProcess>();
        var tokens = Repository<SchemataProcessToken>();
        var transitions = Repository<SchemataProcessTransition>();
        var sources = Repository<SchemataProcessSource>();
        var compensations = Repository<SchemataProcessCompensation>();
        processes.Setup(r => r.Begin()).Returns(uow.Object);
        var services = new ServiceCollection()
                      .AddSingleton(processes.Object)
                      .AddSingleton(tokens.Object)
                      .AddSingleton(transitions.Object)
                      .AddSingleton(sources.Object)
                      .AddSingleton(compensations.Object)
                      .BuildServiceProvider();
        var persistence = new ProcessPersistence();

        await Assert.ThrowsAsync<InvalidOperationException>(() => persistence.ExecuteAsync(
            services,
            static (_, _) => throw new InvalidOperationException("stock update failed"),
            CancellationToken.None));

        uow.Verify(x => x.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Mock<IRepository<T>> Repository<T>(params T[] items)
        where T : class {
        var data = items.ToList();
        var repository = new Mock<IRepository<T>>();
        repository.Setup(r => r.AdviceContext).Returns(new AdviceContext(new ServiceCollection().BuildServiceProvider()));
        repository.Setup(r => r.Join(It.IsAny<IUnitOfWork>()));
        repository.Setup(r => r.Begin()).Returns(Mock.Of<IUnitOfWork>());
        repository.Setup(r => r.AddAsync(It.IsAny<T>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
                  .ReturnsAsync((T entity, CancellationToken _, MutationOptions _) => {
                      FlowTestCreation.Assign(entity);
                      data.Add(entity);
                      return MutationResult.Applied;
                  });
        repository.Setup(r => r.UpdateAsync(It.IsAny<T>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>())).ReturnsAsync(MutationResult.Applied);
        repository.Setup(r => r.ListAsync<T>(It.IsAny<Func<IQueryable<T>, IQueryable<T>>>(), It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns((Func<IQueryable<T>, IQueryable<T>> predicate, CancellationToken _, QueryOptions _) => Async(predicate(data.AsQueryable()).ToList()));
        repository.Setup(r => r.SingleOrDefaultAsync(It.IsAny<Func<IQueryable<T>, IQueryable<T>>>(), It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns((Func<IQueryable<T>, IQueryable<T>> predicate, CancellationToken _, QueryOptions _) => new(predicate(data.AsQueryable()).SingleOrDefault()));
        repository.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Func<IQueryable<T>, IQueryable<T>>>(), It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns((Func<IQueryable<T>, IQueryable<T>> predicate, CancellationToken _, QueryOptions _) => new(predicate(data.AsQueryable()).FirstOrDefault()));
        return repository;
    }

    private static async IAsyncEnumerable<T> Async<T>(IEnumerable<T> items) {
        foreach (var item in items) {
            yield return item;
        }

        await Task.CompletedTask;
    }

    private sealed class InventoryProcess : ProcessDefinition
    {
        public InventoryProcess() {
            this.Start().Go(Reserve);
            this.During(Reserve).OnEnter(async (ctx, ct) => {
                var transactions = ctx.Repository<Transaction>();
                var stocks = ctx.Repository<Stock>();
                var stock = await stocks.SingleOrDefaultAsync(q => q.Where(s => s.CanonicalName == "stocks/stock1"), ct);
                await transactions.AddAsync(new() { Name = "tx1", CanonicalName = "transactions/tx1" }, ct);
                stock!.Quantity--;
                await stocks.UpdateAsync(stock, ct);
            }).Go(Done);
            this.During(Done).End();
        }

        public UserTask Reserve { get; } = null!;

        public UserTask Done { get; } = null!;
    }

    public sealed class Stock : ICanonicalName
    {
        public int Quantity { get; set; }

        public string? Name { get; set; }

        public string? CanonicalName { get; set; }
    }

    public sealed class Transaction : ICanonicalName
    {
        public string? Name { get; set; }

        public string? CanonicalName { get; set; }
    }
}
