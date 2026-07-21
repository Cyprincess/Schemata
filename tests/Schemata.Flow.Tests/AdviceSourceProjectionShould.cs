using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Schemata.Flow.Foundation;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Flow.Foundation.Advisors;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Tests;

public class AdviceSourceProjectionShould
{
    [Fact]
    public async Task Project_Business_State_For_Single_Token_Process_Level_Binding() {
        var definition = Definition(Activity("Paid"));
        var process = Process("Running");
        var token = Token("tokens/t1", "Paid", "Active");
        var order = NewOrder("Created");
        var rows = new List<SchemataProcessSource> { Row(process, order, "order") };
        var harness = Harness(definition, order, rows, Descriptors(State("order")));

        await harness.AdviseAsync(Context(process, token, [token]));

        Assert.Equal("Paid", order.State);
    }

    [Fact]
    public async Task Project_Process_Lifecycle_When_Process_Is_Terminal() {
        var definition = Definition(Activity("Paid"));
        var process = Process("Completed");
        var token = Token("tokens/t1", "Paid", "Active");
        var order = NewOrder("Created");
        var harness = Harness(definition, order, [Row(process, order, "order")], Descriptors(State("order")));

        await harness.AdviseAsync(Context(process, token, [token]));

        Assert.Equal("Completed", order.State);
    }

    [Fact]
    public async Task Skip_Write_When_Landing_Element_Is_Gateway_Or_Synthetic() {
        FlowElement[] elements = [
            new ExclusiveGateway { Name = "Await_Payment" },
            new ProcedureTask { Name = "Enter_Payment" },
            new EndEvent { Name = "End_Payment" },
        ];
        foreach (var element in elements) {
            var definition = Definition(element);
            var process = Process("Running");
            var token = Token("tokens/t1", element.Name, "Active");
            var order = NewOrder("Created");
            var harness = Harness(definition, order, [Row(process, order, "order")], Descriptors(State("order")));

            await harness.AdviseAsync(Context(process, token, [token]));

            Assert.Equal("Created", order.State);
        }
    }

    [Fact]
    public async Task Freeze_Token_Scoped_State_When_Token_Completes() {
        var definition = Definition(Activity("Captured"));
        var process = Process("Completed");
        var token = Token("tokens/t1", "Captured", "Completed");
        var order = NewOrder("Authorized");
        var harness = Harness(definition, order, [Row(process, order, "order", token.CanonicalName)], Descriptors(State("order")));

        await harness.AdviseAsync(Context(process, token, [token]));

        Assert.Equal("Authorized", order.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mirror_Scope_Lifecycle_Into_Lifecycle_Member(bool tokenScoped) {
        var definition = Definition(Activity("Paid"));
        var process = Process("Running");
        var token = Token("tokens/t1", "Paid", "Waiting");
        var order = NewOrder("Created");
        var harness = Harness(definition, order, [Row(process, order, "order", tokenScoped ? token.CanonicalName : null)], Descriptors(WithLifecycle(State("order"))));

        await harness.AdviseAsync(Context(process, token, [token]));

        Assert.Equal(tokenScoped ? "Waiting" : "Running", order.Lifecycle);
    }

    [Fact]
    public async Task Skip_Business_Projection_When_Multiple_Tokens_Active_On_Process_Level_Binding() {
        var logger = new Mock<ILogger<AdviceSourceProjection<Order>>>();
        var definition = Definition(Activity("Paid"));
        var process = Process("Running", $"processes/{Guid.NewGuid():N}");
        var current = Token("tokens/t1", "Paid", "Active");
        var other = Token("tokens/t2", "Shipped", "Active");
        var order = NewOrder("Created");
        var harness = Harness(definition, order, [Row(process, order, "order")], Descriptors(State("order")), logger);

        await harness.AdviseAsync(Context(process, current, [current, other]));

        Assert.Equal("Created", order.State);
        logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((_, _) => true),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task Write_Member_Selected_By_Named_Binding_Declaration() {
        var definition = Definition(Activity("Captured"));
        var process = Process("Running");
        var token = Token("tokens/t1", "Captured", "Active");
        var order = NewOrder("Created");
        var harness = Harness(
            definition,
            order,
            [Row(process, order, "payment")],
            Descriptors(State("payment", get: o => o.PaymentState, set: (o, v) => o.PaymentState = v)));

        await harness.AdviseAsync(Context(process, token, [token]));

        Assert.Equal("Captured", order.PaymentState);
        Assert.Equal("Created", order.State);
    }

    [Fact]
    public async Task Project_Distinct_Members_For_Distinct_Token_Scoped_Bindings_Of_Same_Entity() {
        var definition = Definition(Activity("Captured"), Activity("Dispatched"));
        var process = Process("Running");
        var payment = Token("tokens/payment", "Captured", "Active");
        var shipment = Token("tokens/shipment", "Dispatched", "Active");
        var order = NewOrder("Created");
        var harness = Harness(
            definition,
            order,
            [Row(process, order, "payment", payment.CanonicalName), Row(process, order, "shipment", shipment.CanonicalName)],
            Descriptors(
                State("payment", get: o => o.PaymentState, set: (o, v) => o.PaymentState = v),
                State("shipment", get: o => o.ShipmentState, set: (o, v) => o.ShipmentState = v)));

        await harness.AdviseAsync(Context(process, payment, [payment, shipment]));
        await harness.AdviseAsync(Context(process, shipment, [payment, shipment]));

        Assert.Equal("Captured", order.PaymentState);
        Assert.Equal("Dispatched", order.ShipmentState);
    }

    [Fact]
    public async Task Mirror_Scope_Lifecycle_Under_Lifecycle_Mode() {
        var definition = Definition(Activity("Paid"));
        var process = Process("Running");
        var token = Token("tokens/t1", "Paid", "Waiting");
        var order = NewOrder("Created");
        var harness = Harness(
            definition,
            order,
            [Row(process, order, "order", token.CanonicalName)],
            Descriptors(State("order", FlowSourceProjection.Lifecycle)));

        await harness.AdviseAsync(Context(process, token, [token]));

        Assert.Equal("Waiting", order.State);
    }

    [Fact]
    public async Task Write_Nothing_Under_None_Mode() {
        var definition = Definition(Activity("Paid"));
        var process = Process("Running");
        var token = Token("tokens/t1", "Paid", "Active");
        var order = NewOrder("Created");
        var descriptor = WithLifecycle(State("order", FlowSourceProjection.None));
        var harness = Harness(definition, order, [Row(process, order, "order")], Descriptors(descriptor));

        await harness.AdviseAsync(Context(process, token, [token]));

        Assert.Equal("Created", order.State);
        Assert.Null(order.Lifecycle);
    }

    [Fact]
    public async Task Skip_Terminal_Lifecycle_Under_BusinessState_Mode() {
        var definition = Definition(Activity("Paid"));
        var process = Process("Completed");
        var token = Token("tokens/t1", "Paid", "Completed");
        var order = NewOrder("Paid");
        var harness = Harness(
            definition,
            order,
            [Row(process, order, "order")],
            Descriptors(State("order", FlowSourceProjection.BusinessState)));

        await harness.AdviseAsync(Context(process, token, [token]));

        Assert.Equal("Paid", order.State);
    }

    [Fact]
    public async Task Project_Terminal_Process_State_Under_Terminal_Mode() {
        var definition = Definition(Activity("Paid"));
        var process = Process("Completed");
        var token = Token("tokens/t1", "Paid", "Completed");
        var order = NewOrder("Paid");
        var harness = Harness(
            definition,
            order,
            [Row(process, order, "order")],
            Descriptors(State("order", FlowSourceProjection.Terminal)));

        await harness.AdviseAsync(Context(process, token, [token]));

        Assert.Equal("Completed", order.State);
    }

    [Fact]
    public async Task Project_Business_State_Under_Terminal_Mode_While_Active() {
        var definition = Definition(Activity("Paid"));
        var process = Process("Running");
        var token = Token("tokens/t1", "Paid", "Active");
        var order = NewOrder("Created");
        var harness = Harness(
            definition,
            order,
            [Row(process, order, "order")],
            Descriptors(State("order", FlowSourceProjection.Terminal)));

        await harness.AdviseAsync(Context(process, token, [token]));

        Assert.Equal("Paid", order.State);
    }

    [Fact]
    public async Task Freeze_Token_Scoped_State_Under_Terminal_Mode_When_Token_Completes() {
        var definition = Definition(Activity("Captured"));
        var process = Process("Completed");
        var token = Token("tokens/t1", "Captured", "Completed");
        var order = NewOrder("Authorized");
        var harness = Harness(
            definition,
            order,
            [Row(process, order, "order", token.CanonicalName)],
            Descriptors(State("order", FlowSourceProjection.Terminal)));

        await harness.AdviseAsync(Context(process, token, [token]));

        Assert.Equal("Authorized", order.State);
    }

    [Fact]
    public async Task Refresh_Timestamps_On_All_Binding_Rows_Of_The_Entity() {
        var definition = Definition(Activity("Paid"));
        var process = Process("Running");
        var token = Token("tokens/t1", "Paid", "Active");
        var other = Token("tokens/t2", "Shipped", "Completed");
        var order = NewOrder("Created");
        var rows = new List<SchemataProcessSource> {
            Row(process, order, "order"),
            Row(process, order, "other", other.CanonicalName),
        };
        var harness = Harness(definition, order, rows, Descriptors(State("order"), State("other")));

        await harness.AdviseAsync(Context(process, token, [token, other]));

        Assert.All(rows, row => Assert.Equal(order.Timestamp, row.SourceTimestamp));
        harness.Bindings.Verify(r => r.UpdateAsync(It.IsAny<SchemataProcessSource>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Reject_Update_When_Any_Binding_Row_Timestamp_Is_Stale() {
        var definition = Definition(Activity("Paid"));
        var process = Process("Running");
        var token = Token("tokens/t1", "Paid", "Active");
        var other = Token("tokens/t2", "Shipped", "Active");
        var order = NewOrder("Created");
        var rows = new List<SchemataProcessSource> {
            Row(process, order, "order"),
            Row(process, order, "other", other.CanonicalName, Guid.NewGuid()),
        };
        var harness = Harness(definition, order, rows, Descriptors(State("order"), State("other")));

        await Assert.ThrowsAsync<FailedPreconditionException>(async () => await harness.AdviseAsync(Context(process, token, [token, other])));

        Assert.Equal("Created", order.State);
    }

    [Fact]
    public async Task Skip_Undeclared_Binding_Rows() {
        var definition = Definition(Activity("Paid"));
        var process = Process("Running");
        var token = Token("tokens/t1", "Paid", "Active");
        var order = NewOrder("Created");
        var harness = Harness(definition, order, [Row(process, order, "missing")], Descriptors());

        await harness.AdviseAsync(Context(process, token, [token]));

        Assert.Equal("Created", order.State);
        harness.Sources.Verify(r => r.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Skip_Update_When_Projected_Values_Unchanged() {
        var definition = Definition(Activity("Paid"));
        var process = Process("Running");
        var token = Token("tokens/t1", "Paid", "Active");
        var order = NewOrder("Paid");
        var harness = Harness(definition, order, [Row(process, order, "order")], Descriptors(State("order")));

        await harness.AdviseAsync(Context(process, token, [token]));

        harness.Sources.Verify(r => r.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Leave_Source_Advisor_Idle_For_Sourceless_Process() {
        var definition = Definition(Activity("Paid"));
        var process = Process("Running");
        var token = Token("tokens/t1", "Paid", "Active");
        var order = NewOrder("Created");
        var harness = Harness(definition, order, [], Descriptors(State("order")));

        await harness.AdviseAsync(Context(process, token, [token]));

        Assert.Equal("Created", order.State);
        harness.Sources.Verify(r => r.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ProjectionHarness Harness(
        ProcessDefinition                                  definition,
        Order                                              order,
        List<SchemataProcessSource>                        rows,
        IReadOnlyDictionary<string, FlowSourceDescriptor> descriptors,
        Mock<ILogger<AdviceSourceProjection<Order>>>?      logger = null
    ) {
        return new(definition, order, rows, descriptors, logger);
    }

    private static ProcessDefinition Definition(params FlowElement[] elements) {
        var definition = new ProcessDefinition { Name = "definition" };
        definition.Elements.AddRange(elements);
        return definition;
    }

    private static UserTask Activity(string name) { return new() { Name = name }; }

    private static SchemataProcess Process(string state, string canonicalName = "processes/p1") {
        return new() {
            Name           = "p1",
            CanonicalName  = canonicalName,
            DefinitionName = "definition",
            State          = state,
        };
    }

    private static SchemataProcessToken Token(string canonicalName, string stateName, string state) {
        return new() {
            Name          = canonicalName[(canonicalName.LastIndexOf('/') + 1)..],
            CanonicalName = canonicalName,
            Process       = "p1",
            ScopeName     = "p1",
            StateName     = stateName,
            State         = state,
        };
    }

    private static Order NewOrder(string state) {
        return new() {
            Name          = "o1",
            CanonicalName = "orders/o1",
            Timestamp     = Guid.NewGuid(),
            State         = state,
        };
    }

    private static SchemataProcessSource Row(
        SchemataProcess process,
        Order           order,
        string          name,
        string?         token           = null,
        Guid?           sourceTimestamp = null
    ) {
        return new() {
            Process         = process.CanonicalName!,
            Token           = token,
            Name            = name,
            Source          = order.CanonicalName!,
            SourceType      = typeof(Order).FullName ?? typeof(Order).Name,
            SourceTimestamp = sourceTimestamp ?? order.Timestamp,
        };
    }

    private static FlowTransitionContext Context(
        SchemataProcess                     process,
        SchemataProcessToken                token,
        IReadOnlyList<SchemataProcessToken> tokens
    ) {
        return new() {
            Snapshot = new() { Process = process, Tokens = tokens, Transitions = [] },
            Token = TokenSnapshotFactory.From(token),
            UnitOfWork = Mock.Of<IUnitOfWork>(),
        };
    }

    private static Dictionary<string, FlowSourceDescriptor> Descriptors(params FlowSourceDescriptor[] descriptors) {
        return descriptors.ToDictionary(descriptor => descriptor.BindingName, StringComparer.Ordinal);
    }

    private static FlowSourceDescriptor State(
        string               name,
        FlowSourceProjection projection = FlowSourceProjection.Auto,
        Func<Order, string?>? get        = null,
        Action<Order, string?>? set       = null
    ) {
        get ??= order => order.State;
        set ??= (order, value) => order.State = value;
        return new() {
            BindingName = name,
            SourceType  = typeof(Order),
            Projection  = projection,
            GetState    = source => get((Order)source),
            SetState    = (source, value) => set((Order)source, value),
        };
    }

    private static FlowSourceDescriptor WithLifecycle(FlowSourceDescriptor descriptor) {
        return new() {
            BindingName  = descriptor.BindingName,
            SourceType   = descriptor.SourceType,
            Projection   = descriptor.Projection,
            GetState     = descriptor.GetState,
            SetState     = descriptor.SetState,
            GetLifecycle = source => ((Order)source).Lifecycle,
            SetLifecycle = (source, value) => ((Order)source).Lifecycle = value,
        };
    }

    private sealed class ProjectionHarness
    {
        private readonly AdviceContext _advice;
        private readonly Order         _order;

        public ProjectionHarness(
            ProcessDefinition                                  definition,
            Order                                              order,
            List<SchemataProcessSource>                        rows,
            IReadOnlyDictionary<string, FlowSourceDescriptor> descriptors,
            Mock<ILogger<AdviceSourceProjection<Order>>>?      logger
        ) {
            _order = order;
            Sources = SourceRepository(order);
            Bindings = Repository(rows);
            var registry = new Mock<IProcessRegistry>();
            registry.Setup(r => r.GetRegistration("definition")).Returns(new ProcessRegistration {
                Name          = "definition",
                Definition    = definition,
                Configuration = new() { Name = "definition" },
                SourceTypes   = descriptors,
            });
            Advisor = new(Sources.Object, Bindings.Object, registry.Object);

            var services = new ServiceCollection();
            if (logger is not null) {
                services.AddSingleton(logger.Object);
            }

            _advice = new(services.BuildServiceProvider());
        }

        public AdviceSourceProjection<Order> Advisor { get; }

        public Mock<IRepository<Order>> Sources { get; }

        public Mock<IRepository<SchemataProcessSource>> Bindings { get; }

        public Task AdviseAsync(FlowTransitionContext context) {
            return Advisor.AdviseAsync(_advice, context, _order);
        }
    }

    private static Mock<IRepository<Order>> SourceRepository(Order order) {
        var repository = new Mock<IRepository<Order>>();
        repository.Setup(r => r.Join(It.IsAny<IUnitOfWork>()));
        repository.Setup(r => r.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
                  .Returns((Order entity, CancellationToken _) => {
                      entity.Timestamp = Guid.NewGuid();
                      return Task.CompletedTask;
                  });
        return repository;
    }

    private static Mock<IRepository<SchemataProcessSource>> Repository(List<SchemataProcessSource> rows) {
        var repository = new Mock<IRepository<SchemataProcessSource>>();
        repository.Setup(r => r.Join(It.IsAny<IUnitOfWork>()));
        repository.Setup(r => r.ListAsync<SchemataProcessSource>(
                            It.IsAny<Func<IQueryable<SchemataProcessSource>, IQueryable<SchemataProcessSource>>>(),
                            It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataProcessSource>, IQueryable<SchemataProcessSource>> query, CancellationToken _) =>
                      EnumerateAsync(query(rows.AsQueryable())));
        repository.Setup(r => r.UpdateAsync(It.IsAny<SchemataProcessSource>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        return repository;
    }

    private static async IAsyncEnumerable<T> EnumerateAsync<T>(IEnumerable<T> items) {
        foreach (var item in items) {
            yield return item;
        }

        await Task.CompletedTask;
    }

    public sealed class Order : ICanonicalName, IConcurrency
    {
        public string? Name { get; set; }

        public string? CanonicalName { get; set; }

        public Guid Timestamp { get; set; }

        public string? State { get; set; }

        public string? PaymentState { get; set; }

        public string? ShipmentState { get; set; }

        public string? Lifecycle { get; set; }
    }
}
