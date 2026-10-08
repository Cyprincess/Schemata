using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Errors;
using Schemata.Entity.Repository;
using Schemata.Flow.Foundation;
using Schemata.Flow.Integration.Tests.Fixtures;
using Schemata.Flow.Repository;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Integration.Tests;

public abstract class FlowEffectRecoveryShould
{
    protected abstract IFlowIntegrationFixture CreateFixture(FakeExternalSystem external);

    [Trait("Layer", "Component")]
    [Fact]
    public async Task Query_By_Request_Id_Confirms_The_Effect_Without_A_Second_Call() {
        var external = new FakeExternalSystem { Queryable = true };
        var fixture  = CreateFixture(external);
        var lifetime = (IAsyncLifetime)fixture;
        await lifetime.InitializeAsync();
        try {
            var process = await StartAsync(fixture, await SeedAsync(fixture));

            external.FailNextCommitOnce();
            await Assert.ThrowsAsync<InvalidOperationException>(() => CompleteAsync(fixture, process));

            Assert.Empty(await IntentRowsAsync(fixture));
            Assert.Equal("Review", (await TokenRowAsync(fixture, process)).StateName);

            await CompleteAsync(fixture, process);

            var send = Assert.Single(external.Observed, entry => entry.StartsWith("send:", StringComparison.Ordinal));
            // One query per drive: the recovery drive's query confirms the landed effect, so the
            // body is skipped and no second send is issued.
            var queries = external.Observed.Where(entry => entry.StartsWith("query:", StringComparison.Ordinal)).ToList();
            Assert.Equal(2, queries.Count);
            Assert.All(queries, query => Assert.Equal(send["send:".Length..], query["query:".Length..]));
            Assert.Equal(1, external.Sends);
            Assert.Equal(1, external.Applied);

            var intent = Assert.Single(await IntentRowsAsync(fixture));
            Assert.Equal(send["send:".Length..], intent.RequestId);
            Assert.Equal(FlowEffectState.Completed, intent.State);
            // The intent records the OnEnter task synthesized for the Call activity; the builder names
            // it Enter_{Activity.Name}, so the expectation derives from the model rather than a literal.
            Assert.Equal($"Enter_{new ExternalEffectProcess().Call.Name}", intent.Task);

            var finished = await CompleteAsync(fixture, process);
            Assert.Equal("Completed", finished.Process.State);
        } finally { await lifetime.DisposeAsync(); }
    }

    [Trait("Layer", "Component")]
    [Fact]
    public async Task Resend_With_Same_Request_Id_Is_Deduped_By_The_Receiver() {
        var external = new FakeExternalSystem { Dedupes = true };
        var fixture  = CreateFixture(external);
        var lifetime = (IAsyncLifetime)fixture;
        await lifetime.InitializeAsync();
        try {
            var process = await StartAsync(fixture, await SeedAsync(fixture));

            external.FailNextCommitOnce();
            await Assert.ThrowsAsync<InvalidOperationException>(() => CompleteAsync(fixture, process));

            Assert.Empty(await IntentRowsAsync(fixture));

            await CompleteAsync(fixture, process);

            var sends = external.Observed.Where(entry => entry.StartsWith("send:", StringComparison.Ordinal)).ToList();
            Assert.Equal(2, sends.Count);
            Assert.Equal(sends[0], sends[1]);
            Assert.Equal(2, external.Sends);
            Assert.Equal(1, external.Applied);

            var intent = Assert.Single(await IntentRowsAsync(fixture));
            Assert.Equal(sends[0]["send:".Length..], intent.RequestId);
            Assert.Equal(FlowEffectState.Completed, intent.State);
        } finally { await lifetime.DisposeAsync(); }
    }

    [Trait("Layer", "Component")]
    [Fact]
    public async Task Report_Outcome_Unknown_When_The_Receiver_Supports_Neither_Query_Nor_Dedupe() {
        var external = new FakeExternalSystem { Indeterminate = true };
        var fixture  = CreateFixture(external);
        var lifetime = (IAsyncLifetime)fixture;
        await lifetime.InitializeAsync();
        try {
            var process = await StartAsync(fixture, await SeedAsync(fixture));

            var exception = await Assert.ThrowsAsync<FlowEffectOutcomeUnknownException>(
                () => CompleteAsync(fixture, process));

            var reason = exception.Details?.OfType<ErrorInfoDetail>().FirstOrDefault()?.Reason;
            Assert.Equal(SchemataConstants.ErrorReasons.FlowEffectOutcomeUnknown, reason);

            Assert.Empty(await IntentRowsAsync(fixture));
            Assert.Equal("Review", (await TokenRowAsync(fixture, process)).StateName);
            Assert.Equal(1, external.Applied);

            external.Indeterminate = false;
            var advanced = await CompleteAsync(fixture, process);

            Assert.Equal("Call", (await TokenRowAsync(fixture, process)).StateName);
            Assert.Equal(2, external.Applied);
            var intent = Assert.Single(await IntentRowsAsync(fixture));
            Assert.Equal(FlowEffectState.Completed, intent.State);
        } finally { await lifetime.DisposeAsync(); }
    }

    private static async Task<Order> SeedAsync(IFlowIntegrationFixture fixture) {
        using var scope      = fixture.CreateScope();
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

    private static async Task<SchemataProcess> StartAsync(IFlowIntegrationFixture fixture, Order order) {
        using var scope = fixture.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IProcessRegistry>().RegisterAsync<ExternalEffectProcess>();
        var current = await scope.ServiceProvider.GetRequiredService<IRepository<Order>>().FindAsync([order.Uid]);
        Assert.NotNull(current);
        var runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
        return await runner.StartAsync(typeof(ExternalEffectProcess).Name, current, null, null, CancellationToken.None);
    }

    private static async Task<ProcessSnapshot> CompleteAsync(IFlowIntegrationFixture fixture, SchemataProcess process) {
        using var scope  = fixture.CreateScope();
        var       runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
        return await runner.CompleteAsync(process, null, null, CancellationToken.None);
    }

    private static async Task<List<SchemataFlowEffectIntent>> IntentRowsAsync(IFlowIntegrationFixture fixture) {
        using var scope      = fixture.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataFlowEffectIntent>>();
        var       rows       = new List<SchemataFlowEffectIntent>();
        await foreach (var row in repository.ListAsync<SchemataFlowEffectIntent>(query => query)) {
            rows.Add(row);
        }

        return rows;
    }

    private static async Task<SchemataProcessToken> TokenRowAsync(IFlowIntegrationFixture fixture, SchemataProcess process) {
        using var scope      = fixture.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcessToken>>();
        var       token = await repository.FirstOrDefaultAsync(
            query => query.Where(row => row.Process == process.Name && row.State == "Active"));
        Assert.NotNull(token);
        return token;
    }
}
