using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Expressions.Cel;
using Schemata.Expressions.Skeleton;
using Schemata.Flow.Foundation;
using Schemata.Flow.Integration.Tests.Fixtures;
using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.StateMachine;
using Xunit;

namespace Schemata.Flow.Integration.Tests;

[Trait("Category", "Integration")]
public class ProcessRegistryExpressionShould : IClassFixture<EfCoreFlowFixture>
{
    private readonly EfCoreFlowFixture _fixture;

    public ProcessRegistryExpressionShould(EfCoreFlowFixture fixture) { _fixture = fixture; }

    [Fact]
    public async Task Register_Compiles_Cel_Condition_And_Engine_Takes_Matching_Branch() {
        var order = new Order {
            Uid = Guid.NewGuid(), Name = "o1", CanonicalName = "orders/o1", Timestamp = Guid.NewGuid(), State = "paid",
        };
        var registration = await RegisterDecisionProcessAsync();

        Assert.True(registration.SourceTypes.ContainsKey("order"));

        var snapshot = await AdvanceAsync(registration.Definition, order, "p1");

        Assert.Equal("Paid", snapshot.Tokens[0].StateName);
    }

    [Fact]
    public async Task Compiled_Cel_Condition_Falls_To_Default_Branch_When_False() {
        var order = new Order {
            Uid = Guid.NewGuid(), Name = "o2", CanonicalName = "orders/o2", Timestamp = Guid.NewGuid(), State = "draft",
        };
        var registration = await RegisterDecisionProcessAsync();

        var snapshot = await AdvanceAsync(registration.Definition, order, "p2");

        Assert.Equal("Rejected", snapshot.Tokens[0].StateName);
    }

    [Fact]
    public async Task Register_Rejects_String_Condition_Without_Language() {
        var registry = new ProcessRegistry(Services());

        await Assert.ThrowsAsync<FailedPreconditionException>(
            async () => await registry.RegisterAsync<DecisionProcess>());
    }

    [Fact]
    public async Task Register_Rejects_Unknown_Expression_Language() {
        var registry = new ProcessRegistry(Services());

        await Assert.ThrowsAsync<FailedPreconditionException>(
            async () => await registry.RegisterAsync<DecisionProcess>(configure: c => c.Language = "feel"));
    }

    [Fact]
    public async Task Register_Rejects_Malformed_Expression() {
        var services = new ServiceCollection().AddCelExpressions()
                                              .AddKeyedSingleton<IFlowRuntime>("statemachine", new StateMachineEngine())
                                              .BuildServiceProvider();
        var registry = new ProcessRegistry(services);

        await Assert.ThrowsAsync<InvalidArgumentException>(
            async () => await registry.RegisterAsync<BrokenProcess>(configure: c => c.Language = ExpressionLanguages.Cel));
    }

    private static async Task<ProcessRegistration> RegisterDecisionProcessAsync() {
        var registry = new ProcessRegistry(CelServices());
        await registry.RegisterAsync<DecisionProcess>(configure: c => c.Language = ExpressionLanguages.Cel);
        return registry.GetRegistration(nameof(DecisionProcess))!;
    }

    private static IServiceProvider CelServices() {
        return new ServiceCollection()
              .AddCelExpressions()
              .AddKeyedSingleton<IFlowRuntime>("statemachine", new StateMachineEngine())
              .BuildServiceProvider();
    }

    private static IServiceProvider Services() {
        return new ServiceCollection()
              .AddKeyedSingleton<IFlowRuntime>("statemachine", new StateMachineEngine())
              .BuildServiceProvider();
    }

    private async Task<ProcessSnapshot> AdvanceAsync(ProcessDefinition definition, Order order, string processName) {
        await SeedAsync(order, processName);

        using var scope      = _fixture.CreateScope();
        var       services   = scope.ServiceProvider;
        var       unitOfWork = services.GetRequiredService<IUnitOfWork<TestDbContext>>();
        var       engine     = new StateMachineEngine();
        var       process    = new SchemataProcess { Name = processName, CanonicalName = $"processes/{processName}" };
        var token = new SchemataProcessToken {
            Name          = "a",
            CanonicalName = $"processes/{processName}/tokens/a",
            Process       = processName,
            ScopeName     = processName,
            StateName     = "Review",
            State         = "Active",
        };

        return await engine.AdvanceAsync(definition, process, [token], new(unitOfWork, services));
    }

    private async Task SeedAsync(Order order, string processName) {
        using var scope   = _fixture.CreateScope();
        var       orders  = scope.ServiceProvider.GetRequiredService<IRepository<Order>>();
        var       sources = scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcessSource>>();

        await orders.AddAsync(order);
        await sources.AddAsync(new() {
            Process    = $"processes/{processName}",
            Name       = "order",
            SourceType = typeof(Order).FullName ?? typeof(Order).Name,
            Source     = order.CanonicalName!,
        });
        await orders.CommitAsync();
        await sources.CommitAsync();
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
}
