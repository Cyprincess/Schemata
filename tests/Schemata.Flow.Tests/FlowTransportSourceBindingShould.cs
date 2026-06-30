using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Entity.Repository;
using Schemata.Flow.Foundation;
using Schemata.Flow.Grpc.Internal;
using Schemata.Flow.Http.Internal;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Tests;

public class FlowTransportSourceBindingShould
{
    [Fact]
    public async Task Http_Start_Loads_Source_Entity_And_Persists_Binding() {
        var harness = new Harness();
        var handler = new FlowHttpStartProcessHandler(harness.Runner, new(harness.Resolver.Object, harness.Registry.Object, harness.Services));

        var process = await handler.InvokeAsync(null, Request(), null, Mock.Of<ClaimsPrincipal>(), CancellationToken.None);

        Assert.Equal("approval", process.DefinitionName);
        var source = Assert.Single(harness.Sources);
        Assert.Equal(process.CanonicalName, source.Process);
        Assert.Equal("order", source.Name);
        Assert.Equal(typeof(Order).FullName, source.SourceType);
        Assert.Equal("orders/o1", source.Source);
        Assert.Equal(harness.Order.Timestamp, source.SourceTimestamp);
    }

    [Fact]
    public async Task Grpc_Start_Loads_Source_Entity_And_Persists_Binding() {
        var harness = new Harness();
        var handler = new FlowGrpcStartProcessHandler(harness.Runner, new(harness.Resolver.Object, harness.Registry.Object, harness.Services));

        var process = await handler.InvokeAsync(null, Request(), null, Mock.Of<ClaimsPrincipal>(), CancellationToken.None);

        Assert.Equal("approval", process.DefinitionName);
        var source = Assert.Single(harness.Sources);
        Assert.Equal(process.CanonicalName, source.Process);
        Assert.Equal("order", source.Name);
        Assert.Equal(typeof(Order).FullName, source.SourceType);
        Assert.Equal("orders/o1", source.Source);
    }

    [Fact]
    public async Task Http_Start_Returns_NotFound_When_Source_Name_Does_Not_Resolve() {
        var harness = new Harness(false);
        var handler = new FlowHttpStartProcessHandler(harness.Runner, new(harness.Resolver.Object, harness.Registry.Object, harness.Services));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.InvokeAsync(null, Request(), null, Mock.Of<ClaimsPrincipal>(), CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Grpc_Start_Returns_NotFound_When_Source_Name_Does_Not_Resolve() {
        var harness = new Harness(false);
        var handler = new FlowGrpcStartProcessHandler(harness.Runner, new(harness.Resolver.Object, harness.Registry.Object, harness.Services));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.InvokeAsync(null, Request(), null, Mock.Of<ClaimsPrincipal>(), CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Http_Start_Returns_NotFound_When_Source_Type_Is_Not_Registered() {
        var harness = new Harness(resolvedType: typeof(Customer));
        var handler = new FlowHttpStartProcessHandler(harness.Runner, new(harness.Resolver.Object, harness.Registry.Object, harness.Services));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.InvokeAsync(null, Request(), null, Mock.Of<ClaimsPrincipal>(), CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Grpc_Start_Returns_NotFound_When_Source_Type_Is_Not_Registered() {
        var harness = new Harness(resolvedType: typeof(Customer));
        var handler = new FlowGrpcStartProcessHandler(harness.Runner, new(harness.Resolver.Object, harness.Registry.Object, harness.Services));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.InvokeAsync(null, Request(), null, Mock.Of<ClaimsPrincipal>(), CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Resolve_Start_Binding_By_Convention_Name_Over_Declaration_Order() {
        var harness = new Harness(sourceTypes: new Dictionary<string, FlowSourceDescriptor> {
            ["alternate"] = Descriptor("alternate"),
            ["order"]     = Descriptor("order"),
        });

        await harness.Runner.StartAsync("approval", harness.Order);

        Assert.Equal("order", Assert.Single(harness.Sources).Name);
    }

    [Fact]
    public async Task Reject_Start_When_Source_Binding_Is_Ambiguous() {
        var harness = new Harness(sourceTypes: new Dictionary<string, FlowSourceDescriptor> {
            ["payment"]  = Descriptor("payment"),
            ["shipment"] = Descriptor("shipment"),
        });

        await Assert.ThrowsAsync<FailedPreconditionException>(() => harness.Runner.StartAsync("approval", harness.Order).AsTask());
    }

    [Fact]
    public async Task Run_Source_Advisors_Once_Per_Token_Per_Snapshot() {
        var advisor = new Mock<IFlowSourceAdvisor<Order>>();
        advisor.Setup(a => a.AdviseAsync(
                          It.IsAny<AdviceContext>(),
                          It.IsAny<FlowTransitionContext>(),
                          It.IsAny<Order>(),
                          It.IsAny<CancellationToken>()))
               .ReturnsAsync(AdviseResult.Continue);
        var harness = new Harness(runtime: new DuplicateTransitionRuntime(), sourceAdvisor: advisor.Object);

        await harness.Runner.StartAsync("approval", harness.Order);

        advisor.Verify(a => a.AdviseAsync(
                           It.IsAny<AdviceContext>(),
                           It.IsAny<FlowTransitionContext>(),
                           It.IsAny<Order>(),
                           It.IsAny<CancellationToken>()), Times.Once);
    }

    private static StartProcessInstanceRequest Request() {
        return new() { DefinitionName = "approval", Source = "orders/o1" };
    }

    private sealed class Harness
    {
        public Harness(
            bool                                           resolveSource = true,
            Type?                                          resolvedType = null,
            IReadOnlyDictionary<string, FlowSourceDescriptor>? sourceTypes = null,
            IFlowRuntime?                                   runtime = null,
            IFlowSourceAdvisor<Order>?                      sourceAdvisor = null
        ) {
            Order = new() { Name = "o1", CanonicalName = "orders/o1", Timestamp = Guid.NewGuid() };
            Sources = [];
            Resolver = new(MockBehavior.Strict);
            Resolver.Setup(r => r.Resolve("orders/o1")).Returns(resolveSource ? resolvedType ?? typeof(Order) : null);
            Registry = RegistryMock(sourceTypes);

            var services = new ServiceCollection();
            services.AddSingleton(Repository([Order]).Object);
            services.AddSingleton(Repository(new List<SchemataProcess>(), true).Object);
            services.AddSingleton(Repository(new List<SchemataProcessToken>()).Object);
            services.AddSingleton(Repository(new List<SchemataProcessTransition>()).Object);
            services.AddSingleton(SourceRepository(Sources).Object);
            if (sourceAdvisor is not null) {
                services.AddSingleton(sourceAdvisor);
            }

            services.AddKeyedSingleton<IFlowRuntime>("StateMachine", runtime ?? new Runtime());
            Services = services.BuildServiceProvider();
            Runner = new(Registry.Object, new(), new(), Notifier(), Services);
        }

        public Order Order { get; }

        public List<SchemataProcessSource> Sources { get; }

        public Mock<IResourceTypeResolver> Resolver { get; }

        public Mock<IProcessRegistry> Registry { get; }

        public IServiceProvider Services { get; }

        public FlowRunner Runner { get; }
    }

    private static Mock<IProcessRegistry> RegistryMock(IReadOnlyDictionary<string, FlowSourceDescriptor>? sourceTypes = null) {
        var registration = new ProcessRegistration {
            Name          = "approval",
            Engine        = "StateMachine",
            Definition    = new() { Name = "approval" },
            Configuration = new() { Name = "approval" },
            SourceTypes = sourceTypes ?? new Dictionary<string, FlowSourceDescriptor> { ["order"] = Descriptor("order") },
        };

        var registry = new Mock<IProcessRegistry>(MockBehavior.Strict);
        registry.Setup(r => r.GetRegistration("approval")).Returns(registration);
        return registry;
    }

    private static ProcessLifecycleNotifier Notifier() {
        return new([], [], new NullLogger<ProcessLifecycleNotifier>());
    }

    private static FlowSourceDescriptor Descriptor(string name) {
        return new() {
            BindingName = name,
            SourceType  = typeof(Order),
            Projection  = FlowSourceProjection.Auto,
        };
    }

    private static Mock<IRepository<T>> Repository<T>(List<T> data, bool begin = false)
        where T : class {
        var repository = new Mock<IRepository<T>>();
        var uow = UnitOfWork();
        repository.SetupGet(r => r.AdviceContext).Returns(new AdviceContext(new ServiceCollection().BuildServiceProvider()));
        if (begin) {
            repository.Setup(r => r.Begin()).Returns(uow.Object);
        }

        repository.Setup(r => r.Join(It.IsAny<IUnitOfWork>()));
        repository.Setup(r => r.SuppressQuerySoftDelete()).Returns(Mock.Of<IDisposable>());
        repository.Setup(r => r.SingleOrDefaultAsync(It.IsAny<Func<IQueryable<T>, IQueryable<T>>>(), It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<T>, IQueryable<T>> predicate, CancellationToken _) =>
                      new(predicate(data.AsQueryable()).SingleOrDefault()));
        repository.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Func<IQueryable<T>, IQueryable<T>>>(), It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<T>, IQueryable<T>> predicate, CancellationToken _) =>
                      new(predicate(data.AsQueryable()).FirstOrDefault()));
        repository.Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<T>, IQueryable<T>>>(), It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<T>, IQueryable<T>> predicate, CancellationToken _) => EnumerateAsync(predicate(data.AsQueryable())));
        repository.Setup(r => r.AddAsync(It.IsAny<T>(), It.IsAny<CancellationToken>()))
                  .Returns((T entity, CancellationToken _) => {
                      data.Add(entity);
                      return Task.CompletedTask;
                  });
        repository.Setup(r => r.UpdateAsync(It.IsAny<T>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return repository;
    }

    private static Mock<IRepository<SchemataProcessSource>> SourceRepository(List<SchemataProcessSource> data) {
        return Repository(data);
    }

    private static async IAsyncEnumerable<T> EnumerateAsync<T>(IEnumerable<T> items) {
        foreach (var item in items) {
            yield return item;
        }

        await Task.CompletedTask;
    }

    private static Mock<IUnitOfWork> UnitOfWork() {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(w => w.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(w => w.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(w => w.Dispose());
        uow.Setup(w => w.DisposeAsync()).Returns(ValueTask.CompletedTask);
        return uow;
    }

    private sealed class Runtime : IFlowRuntime
    {
        public string EngineName => "StateMachine";

        public ValueTask<ProcessSnapshot> StartAsync(
            ProcessDefinition definition,
            SchemataProcess   process,
            FlowExecutionContext context,
            CancellationToken ct = default
        ) {
            process.State = "Waiting";
            var token = new SchemataProcessToken {
                Name          = "root",
                CanonicalName = $"{process.CanonicalName}/tokens/root",
                Process       = process.Name!,
                ScopeName     = process.Name!,
                StateName     = "review",
                WaitingAtName = "review",
                State         = "Waiting",
            };
            return new(new ProcessSnapshot { Process = process, Tokens = [token], Transitions = [] });
        }

        public ValueTask<ProcessSnapshot> TriggerAsync(
            ProcessDefinition definition,
            SchemataProcess process,
            IReadOnlyList<SchemataProcessToken> tokens,
            FlowExecutionContext context,
            IEventDefinition trigger,
            object? payload,
            string? tokenName = null,
            CancellationToken ct = default
        ) {
            throw new NotSupportedException();
        }

        public ValueTask<ProcessSnapshot> AdvanceAsync(
            ProcessDefinition definition,
            SchemataProcess process,
            IReadOnlyList<SchemataProcessToken> tokens,
            FlowExecutionContext context,
            string? tokenName = null,
            CancellationToken ct = default
        ) {
            throw new NotSupportedException();
        }

        public ValueTask<IReadOnlyList<string>> FindTriggerTargetsAsync(
            ProcessDefinition definition,
            SchemataProcess process,
            IReadOnlyList<SchemataProcessToken> tokens,
            FlowExecutionContext context,
            IEventDefinition trigger,
            CancellationToken ct = default
        ) {
            throw new NotSupportedException();
        }
    }

    private sealed class DuplicateTransitionRuntime : IFlowRuntime
    {
        public string EngineName => "StateMachine";

        public ValueTask<ProcessSnapshot> StartAsync(
            ProcessDefinition    definition,
            SchemataProcess      process,
            FlowExecutionContext context,
            CancellationToken    ct = default
        ) {
            process.State = "Running";
            var token = new SchemataProcessToken {
                Name          = "root",
                CanonicalName = $"{process.CanonicalName}/tokens/root",
                Process       = process.Name!,
                ScopeName     = process.Name!,
                StateName     = "review",
                State         = "Active",
            };
            return new(new ProcessSnapshot {
                Process = process,
                Tokens = [token],
                Transitions = [
                    new() { Token = token.CanonicalName, Event = "Start" },
                    new() { Token = token.CanonicalName, Event = "Start" },
                ],
            });
        }

        public ValueTask<ProcessSnapshot> TriggerAsync(
            ProcessDefinition                   definition,
            SchemataProcess                     process,
            IReadOnlyList<SchemataProcessToken> tokens,
            FlowExecutionContext                context,
            IEventDefinition                    trigger,
            object?                             payload,
            string?                             tokenName = null,
            CancellationToken                   ct        = default
        ) {
            throw new NotSupportedException();
        }

        public ValueTask<ProcessSnapshot> AdvanceAsync(
            ProcessDefinition                   definition,
            SchemataProcess                     process,
            IReadOnlyList<SchemataProcessToken> tokens,
            FlowExecutionContext                context,
            string?                             tokenName = null,
            CancellationToken                   ct        = default
        ) {
            throw new NotSupportedException();
        }

        public ValueTask<IReadOnlyList<string>> FindTriggerTargetsAsync(
            ProcessDefinition                   definition,
            SchemataProcess                     process,
            IReadOnlyList<SchemataProcessToken> tokens,
            FlowExecutionContext                context,
            IEventDefinition                    trigger,
            CancellationToken                   ct = default
        ) {
            throw new NotSupportedException();
        }
    }

    [CanonicalName("orders/{order}")]
    public sealed class Order : ICanonicalName, IConcurrency
    {
        public string? Name { get; set; }

        public string? CanonicalName { get; set; }

        [ConcurrencyCheck]
        public Guid Timestamp { get; set; }
    }

    [CanonicalName("customers/{customer}")]
    public sealed class Customer : ICanonicalName
    {
        public string? Name { get; set; }

        public string? CanonicalName { get; set; }
    }
}
