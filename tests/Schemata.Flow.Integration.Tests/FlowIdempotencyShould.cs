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
        var process = (await StartAsync(key)).Process!;

        using (var scope = _fixture.CreateScope()) {
            var runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
            await runner.CompleteAsync(process, null, null, CancellationToken.None);
        }

        using (var scope = _fixture.CreateScope()) {
            var processes = scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcess>>();
            var persisted = await processes.FindAsync([process.Uid]);
            Assert.NotNull(persisted);
            Assert.Null(persisted!.IdempotencyKey);
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
                new StartProcessOptions { IdempotencyKey = key },
                CancellationToken.None);
            return new(process, null);
        } catch (Exception ex) {
            return new(null, ex);
        }
    }

    private sealed record StartOutcome(SchemataProcess? Process, Exception? Error);
}

[Trait("Category", "Integration")]
public sealed class EfCoreFlowIdempotencyShould : FlowIdempotencyShould, IClassFixture<EfCoreFlowFixture>
{
    public EfCoreFlowIdempotencyShould(EfCoreFlowFixture fixture) : base(fixture) { }
}

[Trait("Category", "Integration")]
public sealed class LinqToDbProcessIndexShould : IClassFixture<LinqToDbFlowFixture>
{
    private readonly LinqToDbFlowFixture _fixture;

    public LinqToDbProcessIndexShould(LinqToDbFlowFixture fixture) { _fixture = fixture; }

    [Fact]
    public async Task Reject_Duplicate_Definition_And_Idempotency_Key() {
        var key = Guid.NewGuid().ToString("n");
        using (var scope = _fixture.CreateScope()) {
            var processes = scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcess>>();
            await processes.AddAsync(Process(key));
            await processes.CommitAsync();
        }

        using var duplicateScope = _fixture.CreateScope();
        var duplicate = duplicateScope.ServiceProvider.GetRequiredService<IRepository<SchemataProcess>>();
        var error = await Assert.ThrowsAnyAsync<Exception>(() => duplicate.AddAsync(Process(key)));
        Assert.Contains("UNIQUE", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static SchemataProcess Process(string key) {
        var name = Guid.NewGuid().ToString("n");
        return new() {
            Name           = name,
            CanonicalName  = $"processes/{name}",
            DefinitionName = nameof(IdempotencyProcess),
            IdempotencyKey = key,
            State          = "Waiting",
        };
    }
}
