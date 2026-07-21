using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Flow.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Flow.Integration.Tests;

public abstract class SourceWriteBackShould
{
    private readonly IFlowIntegrationFixture _fixture;

    protected SourceWriteBackShould(IFlowIntegrationFixture fixture) { _fixture = fixture; }

    [Fact]
    public async Task Persist_Task_Mutations_To_Bound_Source_On_Commit() {
        var order   = await CreateOrderAsync();
        var process = await StartAsync<PersistTaskMutationProcess>(order);

        await CompleteAsync(process);

        var persisted = await ReadOrderAsync(order.Uid);
        Assert.Equal("task-written", persisted.TaskValue);
    }

    [Fact]
    public async Task Projection_Observes_Task_Mutations_Within_Same_Unit_Of_Work() {
        var order   = await CreateOrderAsync("Review");
        var process = await StartAsync<ProjectionProcess>(order);

        await CompleteAsync(process);

        var persisted = await ReadOrderAsync(order.Uid);
        Assert.Equal("task-written", persisted.TaskValue);
        Assert.Equal("Apply", persisted.State);
    }

    [Fact]
    public async Task Skip_Source_Tracking_For_Condition_Evaluation() {
        var order   = await CreateOrderAsync();
        var process = await StartAsync<ConditionProcess>(order);
        var before  = (await ReadOrderAsync(order.Uid)).Timestamp;

        await CompleteAsync(process);

        var persisted = await ReadOrderAsync(order.Uid);
        Assert.Equal(before, persisted.Timestamp);
    }

    [Fact]
    public async Task Roll_Back_Source_Writes_When_Transition_Fails() {
        var order   = await CreateOrderAsync();
        var process = await StartAsync<FailingTaskProcess>(order);

        await Assert.ThrowsAsync<InvalidOperationException>(() => CompleteAsync(process));

        var persisted = await ReadOrderAsync(order.Uid);
        var bindings  = await ReadBindingsAsync(process.CanonicalName!);
        Assert.Equal("before", persisted.TaskValue);
        Assert.DoesNotContain(bindings, binding => binding.Name == "temporary");
    }

    [Fact]
    public async Task Refresh_Cross_Token_Binding_Rows_On_Branch_Write() {
        var order   = await CreateOrderAsync();
        var process = await StartAsync<BranchWriteProcess>(order);
        var source  = await ReadOrderAsync(order.Uid);
        var branchToken = $"{process.CanonicalName}/tokens/branch";

        await AddBranchBindingAsync(process, source, branchToken);
        var snapshot = await CompleteAsync(process);

        var persisted = await ReadOrderAsync(order.Uid);
        var bindings  = await ReadBindingsAsync(process.CanonicalName!);
        Assert.Equal(2, bindings.Count);
        Assert.All(bindings, binding => Assert.Equal(persisted.Timestamp, binding.SourceTimestamp));

        await RunOtherScopeProjectionAsync(snapshot, persisted, branchToken);
    }

    private async Task<Order> CreateOrderAsync(string state = "new") {
        using var scope      = _fixture.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<Order>>();
        var order = new Order {
            Uid           = Identifiers.NewUid(),
            Name          = Identifiers.NewUid().ToString("n"),
            CanonicalName = $"orders/{Identifiers.NewUid():n}",
            Timestamp     = Identifiers.NewUid(),
            State         = state,
            TaskValue     = "before",
        };

        await repository.AddAsync(order);
        await repository.CommitAsync();
        return order;
    }

    private async Task<SchemataProcess> StartAsync<TProcess>(Order order)
        where TProcess : ProcessDefinition {
        var current = await ReadOrderAsync(order.Uid);
        using var scope  = _fixture.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
        return await runner.StartAsync(typeof(TProcess).Name, current, null, null, CancellationToken.None);
    }

    private async Task<ProcessSnapshot> CompleteAsync(SchemataProcess process) {
        using var scope  = _fixture.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
        return await runner.CompleteAsync(process, null, null, CancellationToken.None);
    }

    private async Task<Order> ReadOrderAsync(Guid uid) {
        using var scope      = _fixture.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<Order>>();
        var order = await repository.FindAsync([uid]);
        Assert.NotNull(order);
        return order!;
    }

    private async Task<List<SchemataProcessSource>> ReadBindingsAsync(string process) {
        using var scope      = _fixture.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcessSource>>();
        var bindings = new List<SchemataProcessSource>();
        await foreach (var binding in repository.ListAsync<SchemataProcessSource>(
                           query => query.Where(source => source.Process == process))) {
            bindings.Add(binding);
        }

        return bindings;
    }

    private async Task AddBranchBindingAsync(SchemataProcess process, Order order, string token) {
        using var scope      = _fixture.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcessSource>>();
        await repository.AddAsync(new() {
            Process         = process.CanonicalName!,
            Token           = token,
            Name            = "order",
            Source          = order.CanonicalName!,
            SourceType      = typeof(Order).FullName!,
            SourceTimestamp = order.Timestamp,
        });
        await repository.CommitAsync();
    }

    private async Task RunOtherScopeProjectionAsync(ProcessSnapshot snapshot, Order order, string branchToken) {
        using var scope      = _fixture.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<Order>>();
        await using var uow  = repository.Begin();
        var advisor = scope.ServiceProvider.GetRequiredService<IFlowSourceAdvisor<Order>>();
        var context = new FlowTransitionContext {
            Snapshot = snapshot,
            Token = new() {
                CanonicalName = branchToken,
                ScopeName     = snapshot.Process.Name!,
                StateName     = "Apply",
                Status        = "Active",
            },
            UnitOfWork = uow,
        };

        var result = await advisor.AdviseAsync(new AdviceContext(scope.ServiceProvider), context, order, CancellationToken.None);
        Assert.Equal(AdviseResult.Continue, result);
        await uow.CommitAsync();
    }
}

[Trait("Category", "Integration")]
public sealed class EfCoreSourceWriteBackShould : SourceWriteBackShould, IClassFixture<EfCoreFlowFixture>
{
    public EfCoreSourceWriteBackShould(EfCoreFlowFixture fixture) : base(fixture) { }
}

[Trait("Category", "Integration")]
public sealed class LinqToDbSourceWriteBackShould : SourceWriteBackShould, IClassFixture<LinqToDbFlowFixture>
{
    public LinqToDbSourceWriteBackShould(LinqToDbFlowFixture fixture) : base(fixture) { }
}
