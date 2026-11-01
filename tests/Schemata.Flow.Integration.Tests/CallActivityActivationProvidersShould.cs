using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.Repository;
using Schemata.Flow.Bpmn.Runtime.SubProcesses;
using Schemata.Flow.Foundation;
using Schemata.Flow.Integration.Tests.Fixtures;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class CallActivityActivationProvidersShould
{
    [Trait("Layer", "Integration")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReactivatedCall_RoundTripsActivationAndRejectsPriorOne(bool linqToDb) {
        IFlowIntegrationFixture fixture;
        if (linqToDb) {
            var typed = new LinqToDbFlowFixture();
            typed.CatchKinds.Add(FlowCatchKind.Message);
            fixture = typed;
        } else {
            var typed = new EfCoreFlowFixture();
            typed.CatchKinds.Add(FlowCatchKind.Message);
            fixture = typed;
        }
        var lifetime = (IAsyncLifetime)fixture;
        await lifetime.InitializeAsync();
        try {
            using (var scope = fixture.CreateScope()) {
                var registry = scope.ServiceProvider.GetRequiredService<IProcessRegistry>();
                await registry.RegisterAsync(new() {
                    Name           = "waiting-child",
                    Engine         = FlowConstants.Engines.Bpmn,
                    DefinitionType = typeof(CallActivityChildRuntimeShould.WaitingChildProcess),
                });
                await registry.RegisterAsync(new() {
                    Name           = "repeating-parent",
                    Engine         = FlowConstants.Engines.Bpmn,
                    DefinitionType = typeof(CallActivityChildRuntimeShould.RepeatingParentProcess),
                });
            }
            SchemataProcess parent;
            using (var scope = fixture.CreateScope()) {
                parent = await scope.ServiceProvider.GetRequiredService<FlowRunner>().StartAsync("repeating-parent", null, CancellationToken.None);
            }
            var first = Assert.Single(await ReadRowsAsync<SchemataProcess>(fixture), row => row.DefinitionName == "waiting-child");
            using (var scope = fixture.CreateScope()) {
                await scope.ServiceProvider.GetRequiredService<FlowRunner>().CorrelateAsync(first, "child-message", (string?)null, null, null, CancellationToken.None);
            }
            for (var i = 0; i < 2; i++) {
                using var scope = fixture.CreateScope();
                var current = (await scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcess>>().FirstOrDefaultAsync(query => query.Where(row => row.CanonicalName == parent.CanonicalName)))!;
                await scope.ServiceProvider.GetRequiredService<FlowRunner>().CompleteAsync(current, null, null, CancellationToken.None);
            }

            var second = Assert.Single(await ReadRowsAsync<SchemataProcess>(fixture), row => row.DefinitionName == "waiting-child" && !TokenStates.IsTerminal(row.State));
            var waiter = Assert.Single(await ReadRowsAsync<SchemataProcessToken>(fixture), row => row.Process == parent.Name && row.WaitingAtName == "call");
            var spawns = (await ReadRowsAsync<SchemataProcessTransition>(fixture))
                         .Where(row => row.Process == parent.Name && row.Kind == TransitionKind.Spawn && row.Note == "call")
                         .ToList();
            var firstSpawn = Assert.Single(spawns, row => row.Posterior == first.CanonicalName);
            var secondSpawn = Assert.Single(spawns, row => row.Posterior == second.CanonicalName);
            Assert.Equal(secondSpawn.Activation, waiter.CurrentCallActivation);
            Assert.NotEqual(firstSpawn.Activation, secondSpawn.Activation);

            using (var scope = fixture.CreateScope()) {
                await scope.ServiceProvider.GetRequiredService<FlowRunner>().CorrelateAsync(second, "child-message", (string?)null, null, null, CancellationToken.None);
            }
            using (var scope = fixture.CreateScope()) {
                var current = (await scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcess>>().FirstOrDefaultAsync(query => query.Where(row => row.CanonicalName == parent.CanonicalName)))!;
                var executor = new CallActivityExecutor<SchemataProcess, SchemataProcessToken, SchemataProcessTransition>(scope.ServiceProvider);
                Assert.Null(await executor.TryCompleteAsync(current, waiter, firstSpawn.Activation, CancellationToken.None));
                var completion = await executor.TryCompleteAsync(current, waiter, secondSpawn.Activation, CancellationToken.None);
                Assert.NotNull(completion);
                Assert.Equal(second.CanonicalName, completion.ChildProcess);
            }
            using (var scope = fixture.CreateScope()) {
                var current = (await scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcess>>().FirstOrDefaultAsync(query => query.Where(row => row.CanonicalName == parent.CanonicalName)))!;
                var snapshot = await scope.ServiceProvider.GetRequiredService<FlowRunner>().CompleteAsync(current, null, null, CancellationToken.None);
                Assert.Equal("again", Assert.Single(snapshot.Tokens, token => token.State == "Active").StateName);
            }
        } finally {
            await lifetime.DisposeAsync();
        }
    }

    private static async Task<List<TEntity>> ReadRowsAsync<TEntity>(IFlowIntegrationFixture fixture) where TEntity : class {
        using var scope = fixture.CreateScope();
        var rows = new List<TEntity>();
        await foreach (var row in scope.ServiceProvider.GetRequiredService<IRepository<TEntity>>().ListAsync<TEntity>(query => query)) {
            rows.Add(row);
        }
        return rows;
    }
}
