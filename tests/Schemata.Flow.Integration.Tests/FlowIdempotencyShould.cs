using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Event.Skeleton.Entities;
using Schemata.Flow.Foundation;
using Schemata.Flow.Integration.Tests.Fixtures;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Integration.Tests;

public abstract class FlowIdempotencyShould
{
    private readonly IFlowIntegrationFixture _fixture;

    protected FlowIdempotencyShould(IFlowIntegrationFixture fixture) { _fixture = fixture; }

    [Fact]
    public async Task Reject_Exactly_One_Of_Two_Parallel_Starts_With_The_Same_Key() {
        var key = Guid.NewGuid().ToString("n");
        var outcomes = await Task.WhenAll(
            Task.Run(() => StartAsync(key)),
            Task.Run(() => StartAsync(key)));

        Assert.Single(outcomes, result => result.Process is not null);
        var failure = Assert.Single(outcomes, result => result.Error is not null).Error;
        Assert.True(failure is AlreadyExistsException or AbortedException, failure?.ToString());

        using var scope = _fixture.CreateScope();
        var processes = scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcess>>();
        var winner = Assert.Single(await processes.ListAsync(query => query.Where(row =>
            row.DefinitionName == nameof(IdempotencyProcess) && row.IdempotencyKey == key)).ToListAsync());
        Assert.Equal(nameof(IdempotencyProcess), winner.DefinitionName);
    }

    [Fact]
    public async Task Reject_AnExistingLiveKey_BeforeCreatingAnotherProcess() {
        var key = Guid.NewGuid().ToString("n");
        var started = await StartAsync(key);
        Assert.Null(started.Error);
        Assert.NotNull(started.Process);

        var duplicate = await StartAsync(key);

        Assert.Null(duplicate.Process);
        Assert.IsType<AlreadyExistsException>(duplicate.Error);
        using var scope = _fixture.CreateScope();
        var processes = scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcess>>();
        Assert.Equal(1, await processes.CountAsync(query => query.Where(row =>
            row.DefinitionName == nameof(IdempotencyProcess) && row.IdempotencyKey == key)));
    }

    [Fact]
    public async Task Release_Idempotency_Key_After_Terminal_Completion() {
        var key     = Guid.NewGuid().ToString("n");
        var started = await StartAsync(key);
        Assert.Null(started.Error);
        Assert.NotNull(started.Process);
        var process = started.Process;

        using (var scope = _fixture.CreateScope()) {
            var runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
            await runner.CompleteAsync(process, null, null, CancellationToken.None);
        }

        using (var scope = _fixture.CreateScope()) {
            var processes = scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcess>>();
            var persisted = await processes.FindAsync([process.Uid]);
            Assert.NotNull(persisted);
            Assert.Null(persisted.IdempotencyKey);
            Assert.Equal(key, persisted.Annotations["schemata/flow/idempotency-key"]);
        }

        var restart = await StartAsync(key);
        Assert.NotNull(restart.Process);
    }

    [Fact]
    public async Task ConcurrentEffectWrites_ForInsertAndUpdate_LeaveOneRequestRow() {
        var requestId = $"effect-{Guid.NewGuid():n}";
        await AssertWriterRaceAsync<SchemataFlowEffectIntent>(
            () => new() {
                RequestId = requestId,
                Process = "processes/process",
                Token = "processes/process/tokens/token",
                Task = "task",
                State = FlowEffectState.Recorded,
            },
            row => row.RequestId == requestId,
            row => row.State = FlowEffectState.Completed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("processes/process/tokens/token")]
    public async Task ConcurrentSourceWrites_ForInsertAndUpdate_LeaveOneBindingRow(string? token) {
        await AssertWriterRaceAsync<SchemataProcessSource>(
            () => new() {
                Process = "processes/process",
                Token = token,
                Name = "input",
                SourceType = "System.String",
                Source = "orders/original",
            },
            row => row.Process == "processes/process" && row.Token == token && row.Name == "input",
            row => row.Source = "orders/updated");
    }

    [Fact]
    public async Task ConcurrentEventWrites_ForInsertAndUpdate_LeaveOneSubscriptionRow() {
        var subscriptionId = $"subscription-{Guid.NewGuid():n}";
        await AssertWriterRaceAsync<SchemataEventSubscription>(
            () => new() {
                Name = subscriptionId,
                SubscriptionId = subscriptionId,
                EventType = "message",
                Target = "processes/process",
                Token = "processes/process/tokens/token",
            },
            row => row.SubscriptionId == subscriptionId,
            row => row.CorrelationKey = "updated");
    }

    private async Task AssertWriterRaceAsync<TEntity>(
        Func<TEntity> create,
        Expression<Func<TEntity, bool>> match,
        Action<TEntity> update)
        where TEntity : class {
        var inserted = await Task.WhenAll(
            WriteAsync(create(), match, update),
            WriteAsync(create(), match, update));
        Assert.All(inserted, AssertAllowedConflict);
        Assert.Equal(1, await CountAsync(match));

        var updated = await Task.WhenAll(
            WriteAsync(create(), match, update),
            WriteAsync(create(), match, update));
        Assert.All(updated, AssertAllowedConflict);
        Assert.Equal(1, await CountAsync(match));
    }

    private async Task<Exception?> WriteAsync<TEntity>(
        TEntity candidate,
        Expression<Func<TEntity, bool>> match,
        Action<TEntity> update)
        where TEntity : class {
        using var scope = _fixture.CreateScope();
        var persistence = new ProcessPersistence();
        try {
            await persistence.ExecuteAsync(scope.ServiceProvider, async (flow, ct) => {
                var repository = scope.ServiceProvider.GetRequiredService<IRepository<TEntity>>();
                repository.Join(flow.UnitOfWork);
                var existing = await repository.FirstOrDefaultAsync(query => query.Where(match), ct);
                var mutation = scope.ServiceProvider.GetRequiredService<IResourceMutation<TEntity>>();
                if (existing is null) {
                    await mutation.CreateAsync(candidate, flow.UnitOfWork, ct);
                } else {
                    update(existing);
                    await mutation.UpdateAsync(existing, flow.UnitOfWork, ct: ct);
                }
            }, CancellationToken.None);
            return null;
        } catch (Exception exception) {
            return exception;
        }
    }

    private async Task<int> CountAsync<TEntity>(Expression<Func<TEntity, bool>> match)
        where TEntity : class {
        using var scope = _fixture.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<TEntity>>();
        return await repository.CountAsync(query => query.Where(match));
    }

    private static void AssertAllowedConflict(Exception? error) {
        Assert.True(error is null or AlreadyExistsException or AbortedException, error?.ToString());
    }

    private async Task<StartOutcome> StartAsync(string key) {
        try {
            using var scope  = _fixture.CreateScope();
            var       runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
            var process = await runner.StartAsync(
                nameof(IdempotencyProcess),
                new() { IdempotencyKey = key },
                CancellationToken.None);
            return new(process, null);
        } catch (Exception ex) {
            return new(null, ex);
        }
    }

    private sealed record StartOutcome(SchemataProcess? Process, Exception? Error);
}

[Trait("Category", "Integration")]
public sealed class LinqToDbFlowIdempotencyShould : FlowIdempotencyShould, IClassFixture<LinqToDbFlowFixture>
{
    public LinqToDbFlowIdempotencyShould(LinqToDbFlowFixture fixture) : base(fixture) { }
}
