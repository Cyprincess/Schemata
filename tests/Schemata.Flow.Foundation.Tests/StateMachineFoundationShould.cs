using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.StateMachine;
using Xunit;

namespace Schemata.Flow.Foundation.Tests;

public class StateMachineFoundationShould
{
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

        var snapshot = await engine.StartAsync(definition, process, new FlowExecutionContext(uow, services));

        Assert.Equal(definition.Reserve.Name, snapshot.Tokens[0].StateName);
        transactions.Verify(r => r.Join(uow), Times.Once);
        stocks.Verify(r => r.Join(uow), Times.Once);
        transactions.Verify(r => r.AddAsync(It.Is<Transaction>(t => t.CanonicalName == "transactions/tx1"), It.IsAny<CancellationToken>()), Times.Once);
        stocks.Verify(r => r.UpdateAsync(It.Is<Stock>(s => s.Quantity == 2), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Roll_Back_Unit_Of_Work_When_Flow_Work_Fails() {
        var uow = new Mock<IUnitOfWork>();
        var processes = Repository<SchemataProcess>();
        var tokens = Repository<SchemataProcessToken>();
        var transitions = Repository<SchemataProcessTransition>();
        var sources = Repository<SchemataProcessSource>();
        processes.Setup(r => r.Begin()).Returns(uow.Object);
        var services = new ServiceCollection()
                      .AddSingleton(processes.Object)
                      .AddSingleton(tokens.Object)
                      .AddSingleton(transitions.Object)
                      .AddSingleton(sources.Object)
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
        repository.Setup(r => r.Join(It.IsAny<IUnitOfWork>()));
        repository.Setup(r => r.Begin()).Returns(Mock.Of<IUnitOfWork>());
        repository.Setup(r => r.AddAsync(It.IsAny<T>(), It.IsAny<CancellationToken>()))
                  .Returns((T entity, CancellationToken _) => {
                      data.Add(entity);
                      return Task.CompletedTask;
                  });
        repository.Setup(r => r.UpdateAsync(It.IsAny<T>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.ListAsync<T>(It.IsAny<Func<IQueryable<T>, IQueryable<T>>>(), It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<T>, IQueryable<T>> predicate, CancellationToken _) => Async(predicate(data.AsQueryable()).ToList()));
        repository.Setup(r => r.SingleOrDefaultAsync(It.IsAny<Func<IQueryable<T>, IQueryable<T>>>(), It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<T>, IQueryable<T>> predicate, CancellationToken _) => new ValueTask<T?>(predicate(data.AsQueryable()).SingleOrDefault()));
        repository.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Func<IQueryable<T>, IQueryable<T>>>(), It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<T>, IQueryable<T>> predicate, CancellationToken _) => new ValueTask<T?>(predicate(data.AsQueryable()).FirstOrDefault()));
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
            this.During(Reserve).OnEnter(async ctx => {
                var transactions = ctx.Repository<Transaction>();
                var stocks = ctx.Repository<Stock>();
                var stock = await stocks.SingleOrDefaultAsync(q => q.Where(s => s.CanonicalName == "stocks/stock1"));
                await transactions.AddAsync(new Transaction { Name = "tx1", CanonicalName = "transactions/tx1" });
                stock!.Quantity--;
                await stocks.UpdateAsync(stock);
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
