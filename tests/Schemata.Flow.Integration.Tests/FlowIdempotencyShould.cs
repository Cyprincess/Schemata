using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Flow.Foundation;
using Schemata.Flow.Integration.Tests.Fixtures;
using Schemata.Flow.Skeleton.Entities;
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
        Assert.IsType<AlreadyExistsException>(failure);
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