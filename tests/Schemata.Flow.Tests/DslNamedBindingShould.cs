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
using Schemata.Flow.StateMachine;
using Xunit;

namespace Schemata.Flow.Tests;

public class DslNamedBindingShould
{
    [Fact]
    public async Task When_With_Explicit_Name_Reads_Binding_Under_That_Name() {
        var definition = new NamedDecisionProcess();
        var engine     = new StateMachineEngine();
        var order      = new Order { Name = "o1", CanonicalName = "orders/o1", State = "paid" };
        var services = new ServiceCollection()
                      .AddSingleton(Repository(new SchemataProcessSource {
                          Process = "processes/p1", Name = "primary_order", SourceType = typeof(Order).FullName ?? typeof(Order).Name, Source = order.CanonicalName!,
                      }).Object)
                      .AddSingleton(Repository(order).Object)
                      .BuildServiceProvider();
        var process = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1" };
        var token   = Token("processes/p1/tokens/a", definition.Review.Name);

        var snapshot = await engine.AdvanceAsync(definition, process, [token], new(Mock.Of<IUnitOfWork>(), services));

        Assert.Equal(definition.Paid.Name, snapshot.Tokens[0].StateName);
    }

    [Fact]
    public void On_Enter_Without_Name_Generates_Task_Name_From_Activity() {
        var definition = new AnonymousProcedureProcess();

        var names = definition.Elements.OfType<ProcedureTask>().Select(t => t.Name).ToList();

        Assert.Contains("Enter_Current", names);
        Assert.Contains("Leave_Current", names);
    }

    [Fact]
    public async Task On_Enter_Typed_Resolves_Source_Into_Body() {
        var definition = new TypedEnterProcess();
        var engine     = new StateMachineEngine();
        var order      = new Order { Name = "o1", CanonicalName = "orders/o1", State = "paid" };
        var services = new ServiceCollection()
                      .AddSingleton(Repository(new SchemataProcessSource {
                          Process = "processes/p1", Name = "order", SourceType = typeof(Order).FullName ?? typeof(Order).Name, Source = order.CanonicalName!,
                      }).Object)
                      .AddSingleton(Repository(order).Object)
                      .BuildServiceProvider();
        var process = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1" };

        await engine.StartAsync(definition, process, new(Mock.Of<IUnitOfWork>(), services));

        Assert.Equal(["orders/o1"], definition.Seen);
    }

    private static SchemataProcessToken Token(string canonical, string stateName, string? waitingAtName = null) {
        return new() {
            Name          = canonical[(canonical.LastIndexOf('/') + 1)..],
            CanonicalName = canonical,
            Process       = "p1",
            ScopeName     = "p1",
            StateName     = stateName,
            WaitingAtName = waitingAtName,
            State         = waitingAtName is not null ? "Waiting" : "Active",
        };
    }

    private static Mock<IRepository<T>> Repository<T>(params T[] items)
        where T : class {
        var data = items.ToList();
        var repository = new Mock<IRepository<T>>();
        repository.Setup(r => r.Join(It.IsAny<IUnitOfWork>()));
        repository.Setup(r => r.AdviceContext).Returns(new AdviceContext(new ServiceCollection().BuildServiceProvider()));
        repository.Setup(r => r.SingleOrDefaultAsync(It.IsAny<Func<IQueryable<T>, IQueryable<T>>>(), It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<T>, IQueryable<T>> predicate, CancellationToken _) => new(predicate(data.AsQueryable()).SingleOrDefault()));
        return repository;
    }

    #region Nested type: NamedDecisionProcess

    private sealed class NamedDecisionProcess : ProcessDefinition
    {
        public NamedDecisionProcess() {
            this.Start().Go(Review);
            this.During(Review).Decide(
                this.When<Order>("primary_order", order => order.State == "paid").Go(Paid),
                this.Otherwise().Go(Rejected));
            this.During(Paid).End();
            this.During(Rejected).End();
        }

        public UserTask Review { get; } = null!;

        public UserTask Paid { get; } = null!;

        public UserTask Rejected { get; } = null!;
    }

    #endregion

    #region Nested type: AnonymousProcedureProcess

    private sealed class AnonymousProcedureProcess : ProcessDefinition
    {
        public AnonymousProcedureProcess() {
            this.Start().Go(Current);
            this.During(Current)
                .OnEnter(_ => ValueTask.CompletedTask)
                .OnLeave(_ => ValueTask.CompletedTask)
                .Go(Next);
            this.During(Next).End();
        }

        public UserTask Current { get; } = null!;

        public UserTask Next { get; } = null!;
    }

    #endregion

    #region Nested type: TypedEnterProcess

    private sealed class TypedEnterProcess : ProcessDefinition
    {
        public TypedEnterProcess() {
            this.Start().Go(Current);
            this.During(Current).OnEnter<Order>((_, order) => {
                Seen.Add(order.CanonicalName!);
                return ValueTask.CompletedTask;
            }).End();
        }

        public List<string> Seen { get; } = [];

        public UserTask Current { get; } = null!;
    }

    #endregion

    #region Nested type: Order

    public sealed class Order : ICanonicalName
    {
        public string? State { get; set; }

        public string? Name { get; set; }

        public string? CanonicalName { get; set; }
    }

    #endregion
}
