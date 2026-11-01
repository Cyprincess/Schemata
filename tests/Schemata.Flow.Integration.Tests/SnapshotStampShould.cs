using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.Repository;
using Schemata.Flow.Foundation;
using Schemata.Flow.Integration.Tests.Fixtures;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Integration.Tests;

public abstract class SnapshotStampShould
{
    private readonly IFlowIntegrationFixture _fixture;

    protected SnapshotStampShould(IFlowIntegrationFixture fixture) { _fixture = fixture; }

    [Trait("Layer", "Component")]
    [Fact]
    public async Task Expose_Durable_Stamps_On_Returned_Snapshot_And_Observer() {
        var order   = await CreateOrderAsync();
        var process = await StartAsync<PersistTaskMutationProcess>(order);
        var before  = await ReadProcessRowAsync(process.CanonicalName!);

        // The first completion runs Apply's task and parks the token there; the update path is
        // exercised while the process is still live.
        var parked = await CompleteAsync(process);
        var mid    = await ReadProcessRowAsync(process.CanonicalName!);
        Assert.NotEqual(before.Timestamp, mid.Timestamp);
        Assert.Equal(mid.Timestamp, parked.Process.Timestamp);

        // The second completion consumes the token at the End event; only now is the process terminal.
        var finished = await CompleteAsync(process);
        Assert.Equal("Completed", finished.Process.State);

        var row    = await ReadProcessRowAsync(process.CanonicalName!);
        var tokens = await ReadTokenRowsAsync(process.Name!);

        // The update rotated the stamp, so the exposed value must be the rotated one.
        Assert.NotEqual(mid.Timestamp, row.Timestamp);
        Assert.Equal(row.Timestamp, finished.Process.Timestamp);

        Assert.Equal(finished.Tokens.Count, tokens.Count);
        var reloaded = tokens.ToDictionary(token => token.CanonicalName!);
        foreach (var token in finished.Tokens) {
            Assert.Equal(reloaded[token.CanonicalName!].Timestamp, token.Timestamp);
        }

        var observer = Capture();
        Assert.Equal(before.Timestamp, observer.Started[process.CanonicalName!]);
        Assert.Equal(row.Timestamp, observer.Transitioned[process.CanonicalName!]);
        Assert.Equal(row.Timestamp, observer.Terminated[process.CanonicalName!]);
    }

    [Trait("Layer", "Component")]
    [Fact]
    public async Task Not_Claim_Uncommitted_Stamps_When_Commit_Fails() {
        var order   = await CreateOrderAsync();
        var process = await StartAsync<FailingSavePreparationProcess>(order);
        var before  = await ReadProcessRowAsync(process.CanonicalName!);

        await Assert.ThrowsAsync<InvalidOperationException>(() => CompleteAsync(process));

        var row = await ReadProcessRowAsync(process.CanonicalName!);
        Assert.Equal(before.Timestamp, row.Timestamp);
        Assert.Equal(before.Timestamp, Capture().Failed[process.CanonicalName!]);
    }

    private StampCaptureObserver Capture() {
        using var scope = _fixture.CreateScope();
        return scope.ServiceProvider.GetServices<IProcessLifecycleObserver>().OfType<StampCaptureObserver>().Single();
    }

    private async Task<Order> CreateOrderAsync() {
        using var scope      = _fixture.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<Order>>();
        var order = new Order {
            Uid           = Guid.NewGuid(),
            Name          = Guid.NewGuid().ToString("n"),
            CanonicalName = $"orders/{Guid.NewGuid():n}",
            Timestamp     = Guid.NewGuid(),
            State         = "new",
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
        var       runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
        return await runner.StartAsync(typeof(TProcess).Name, current, null, null, CancellationToken.None);
    }

    private async Task<ProcessSnapshot> CompleteAsync(SchemataProcess process) {
        using var scope  = _fixture.CreateScope();
        var       runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
        return await runner.CompleteAsync(process, null, null, CancellationToken.None);
    }

    private async Task<Order> ReadOrderAsync(Guid uid) {
        using var scope      = _fixture.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<Order>>();
        var       order      = await repository.FindAsync([uid]);
        Assert.NotNull(order);
        return order;
    }

    private async Task<SchemataProcess> ReadProcessRowAsync(string canonicalName) {
        using var scope      = _fixture.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcess>>();
        var       process    = await repository.FirstOrDefaultAsync(
            query => query.Where(row => row.CanonicalName == canonicalName));
        Assert.NotNull(process);
        return process;
    }

    // Token rows key their Process column by the process Name, matching GetTokenIndexAsync.
    private async Task<List<SchemataProcessToken>> ReadTokenRowsAsync(string processName) {
        using var scope      = _fixture.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcessToken>>();
        var       tokens     = new List<SchemataProcessToken>();
        await foreach (var token in repository.ListAsync<SchemataProcessToken>(
                           query => query.Where(row => row.Process == processName))) {
            tokens.Add(token);
        }

        return tokens;
    }
}
