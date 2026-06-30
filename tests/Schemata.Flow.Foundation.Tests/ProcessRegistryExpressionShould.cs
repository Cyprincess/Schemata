using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Expressions.Cel;
using Schemata.Expressions.Skeleton;
using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.StateMachine;
using Xunit;

namespace Schemata.Flow.Foundation.Tests;

public class ProcessRegistryExpressionShould
{
    [Fact]
    public async Task Register_Compiles_Cel_Condition_And_Engine_Takes_Matching_Branch() {
        var order    = new Order { Name = "o1", CanonicalName = "orders/o1", State = "paid" };
        var services = Services(order);
        var registry = new ProcessRegistry(services);

        await registry.RegisterAsync<DecisionProcess>(configure: c => c.Language = ExpressionLanguages.Cel);
        var registration = registry.GetRegistration(nameof(DecisionProcess))!;

        Assert.True(registration.SourceTypes.ContainsKey("order"));

        var snapshot = await Advance(registration.Definition, services);

        Assert.Equal("Paid", snapshot.Tokens[0].StateName);
    }

    [Fact]
    public async Task Compiled_Cel_Condition_Falls_To_Default_Branch_When_False() {
        var order    = new Order { Name = "o1", CanonicalName = "orders/o1", State = "draft" };
        var services = Services(order);
        var registry = new ProcessRegistry(services);

        await registry.RegisterAsync<DecisionProcess>(configure: c => c.Language = ExpressionLanguages.Cel);
        var registration = registry.GetRegistration(nameof(DecisionProcess))!;

        var snapshot = await Advance(registration.Definition, services);

        Assert.Equal("Rejected", snapshot.Tokens[0].StateName);
    }

    [Fact]
    public async Task Register_Rejects_String_Condition_Without_Language() {
        var registry = new ProcessRegistry(new ServiceCollection().BuildServiceProvider());

        await Assert.ThrowsAsync<FailedPreconditionException>(
            async () => await registry.RegisterAsync<DecisionProcess>());
    }

    [Fact]
    public async Task Register_Rejects_Unknown_Expression_Language() {
        var registry = new ProcessRegistry(new ServiceCollection().BuildServiceProvider());

        await Assert.ThrowsAsync<FailedPreconditionException>(
            async () => await registry.RegisterAsync<DecisionProcess>(configure: c => c.Language = "feel"));
    }

    [Fact]
    public async Task Register_Rejects_Malformed_Expression() {
        var services = new ServiceCollection().AddCelExpressions().BuildServiceProvider();
        var registry = new ProcessRegistry(services);

        await Assert.ThrowsAsync<InvalidArgumentException>(
            async () => await registry.RegisterAsync<BrokenProcess>(configure: c => c.Language = ExpressionLanguages.Cel));
    }

    private static IServiceProvider Services(Order order) {
        return new ServiceCollection()
              .AddCelExpressions()
              .AddSingleton(Repository(new SchemataProcessSource {
                   Process = "processes/p1", Name = "order", SourceType = typeof(Order).FullName ?? typeof(Order).Name, Source = order.CanonicalName!,
               }).Object)
              .AddSingleton(Repository(order).Object)
              .BuildServiceProvider();
    }

    private static async Task<ProcessSnapshot> Advance(ProcessDefinition definition, IServiceProvider services) {
        var engine  = new StateMachineEngine();
        var process = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1" };
        var token = new SchemataProcessToken {
            Name          = "a",
            CanonicalName = "processes/p1/tokens/a",
            Process       = "p1",
            ScopeName     = "p1",
            StateName     = "Review",
            State         = "Active",
        };

        return await engine.AdvanceAsync(definition, process, [token], new(Mock.Of<IUnitOfWork>(), services));
    }

    private static Mock<IRepository<T>> Repository<T>(params T[] items)
        where T : class {
        var data = items.ToList();
        var repository = new Mock<IRepository<T>>();
        repository.Setup(r => r.Join(It.IsAny<IUnitOfWork>()));
        repository.Setup(r => r.SingleOrDefaultAsync(It.IsAny<Func<IQueryable<T>, IQueryable<T>>>(), It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<T>, IQueryable<T>> predicate, CancellationToken _) => new(predicate(data.AsQueryable()).SingleOrDefault()));
        return repository;
    }

    private sealed class DecisionProcess : ProcessDefinition
    {
        public DecisionProcess() {
            BindSource<Order>();
            this.Start().Go(Review);
            this.During(Review).Decide(
                this.When<Order>("state == 'paid'").Go(Paid),
                this.Otherwise().Go(Rejected));
            this.During(Paid).End();
            this.During(Rejected).End();
        }

        public UserTask Review { get; } = null!;

        public UserTask Paid { get; } = null!;

        public UserTask Rejected { get; } = null!;
    }

    private sealed class BrokenProcess : ProcessDefinition
    {
        public BrokenProcess() {
            BindSource<Order>();
            this.Start().Go(Review);
            this.During(Review).Decide(
                this.When<Order>("state ==").Go(Paid),
                this.Otherwise().Go(Paid));
            this.During(Paid).End();
        }

        public UserTask Review { get; } = null!;

        public UserTask Paid { get; } = null!;
    }

    public sealed class Order : ICanonicalName
    {
        public string? State { get; set; }

        public string? Name { get; set; }

        public string? CanonicalName { get; set; }
    }
}
