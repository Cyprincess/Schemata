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
using Xunit;

namespace Schemata.Flow.Integration.Tests;

public abstract class CompensationPersistenceShould
{
    private readonly IFlowIntegrationFixture _fixture;

    protected CompensationPersistenceShould(IFlowIntegrationFixture fixture) { _fixture = fixture; }

    [Fact]
    public async Task Persist_Bindings_And_Execute_Compensation_After_A_Fresh_Scope_Reload() {
        var process = await StartAsync<CompensationReloadProcess>();

        await CompleteAsync(process);

        var bindings = await ReadBindingsAsync(process.CanonicalName!);
        var binding  = Assert.Single(bindings);
        Assert.Equal(process.CanonicalName, binding.Process);
        Assert.Equal(process.CanonicalName, binding.ScopeOwnerCanonicalName);
        Assert.Equal("host", binding.ActivityName);
        Assert.Equal(0, binding.RegistrationOrder);
        Assert.StartsWith("consumer-", binding.Name);
        Assert.Equal($"process-compensations/{binding.Name}", binding.CanonicalName);

        var compensated = await CompleteAsync(process);

        var transition = Assert.Single(compensated.Transitions, current => current.Kind == TransitionKind.Compensate);
        Assert.Equal("host", transition.Previous);
        Assert.Equal("undo-host", transition.Posterior);
        Assert.StartsWith("consumer-", transition.Name);
        Assert.Contains(compensated.Tokens, token => token.CanonicalName == transition.Token);
        Assert.Empty(await ReadBindingsAsync(process.CanonicalName!));
    }

    [Fact]
    public async Task Remove_Bindings_When_A_Process_Reaches_Terminal_Completion() {
        var process = await StartAsync<CompensationTerminalProcess>();

        await CompleteAsync(process);
        Assert.Single(await ReadBindingsAsync(process.CanonicalName!));

        await CompleteAsync(process);
        var completed = await CompleteAsync(process);

        Assert.Equal("Completed", completed.Process.State);
        Assert.Empty(await ReadBindingsAsync(process.CanonicalName!));
    }

    [Fact]
    public async Task Retain_Binding_Uid_And_Consumer_Name_When_Snapshot_Is_Unchanged() {
        var process = await StartAsync<CompensationReloadProcess>();
        await CompleteAsync(process);
        var before = Assert.Single(await ReadBindingsAsync(process.CanonicalName!));
        using (var services = _fixture.CreateScope()) {
            var persistence = services.ServiceProvider.GetRequiredService<ProcessPersistence>();
            await persistence.ExecuteAsync(services.ServiceProvider, async (scope, ct) => {
                var stored = await scope.Processes.FirstOrDefaultAsync(q => q.Where(p => p.CanonicalName == process.CanonicalName), ct);
                await persistence.PersistSnapshotAsync(scope, new() {
                    Process = stored!, Tokens = [], Transitions = [],
                    CompensationBindings = [new(before.ScopeOwnerCanonicalName, before.ActivityName, before.BoundaryName, before.RegistrationOrder)],
                }, ct);
            }, CancellationToken.None);
        }
        var after = Assert.Single(await ReadBindingsAsync(process.CanonicalName!));
        Assert.Equal(before.Uid, after.Uid);
        Assert.Equal(before.Name, after.Name);
        Assert.Equal(before.CanonicalName, after.CanonicalName);
    }

    [Fact]
    public async Task Restore_A_Removed_Binding_In_The_Same_Unit_Of_Work() {
        var process = await StartAsync<CompensationReloadProcess>();
        await CompleteAsync(process);
        var binding = Assert.Single(await ReadBindingsAsync(process.CanonicalName!));
        using (var services = _fixture.CreateScope()) {
            var persistence = services.ServiceProvider.GetRequiredService<ProcessPersistence>();
            await persistence.ExecuteAsync(services.ServiceProvider, async (scope, ct) => {
                var stored = await scope.Processes.FirstOrDefaultAsync(q => q.Where(p => p.CanonicalName == process.CanonicalName), ct);
                await persistence.PersistSnapshotAsync(scope, new() { Process = stored!, Tokens = [], Transitions = [], CompensationBindings = [] }, ct);
                await persistence.PersistSnapshotAsync(scope, new() {
                    Process = stored!, Tokens = [], Transitions = [],
                    CompensationBindings = [new(binding.ScopeOwnerCanonicalName, binding.ActivityName, binding.BoundaryName, binding.RegistrationOrder)],
                }, ct);
            }, CancellationToken.None);
        }
        var restored = Assert.Single(await ReadBindingsAsync(process.CanonicalName!));
        Assert.Equal(binding.ActivityName, restored.ActivityName);
        Assert.Equal(binding.ScopeOwnerCanonicalName, restored.ScopeOwnerCanonicalName);
        Assert.Equal(binding.RegistrationOrder, restored.RegistrationOrder);
        Assert.StartsWith("consumer-", restored.Name);
    }

    private async Task<SchemataProcess> StartAsync<TProcess>()
        where TProcess : ProcessDefinition {
        using var scope  = _fixture.CreateScope();
        var       runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
        return await runner.StartAsync(typeof(TProcess).Name, null, null, CancellationToken.None);
    }

    private async Task<ProcessSnapshot> CompleteAsync(SchemataProcess process) {
        using var scope       = _fixture.CreateScope();
        var       runner      = scope.ServiceProvider.GetRequiredService<FlowRunner>();
        var       repository  = scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcess>>();
        var persisted = await repository.FirstOrDefaultAsync(
                            query => query.Where(current => current.CanonicalName == process.CanonicalName));
        Assert.NotNull(persisted);
        return await runner.CompleteAsync(persisted, null, null, CancellationToken.None);
    }

    private async Task<List<SchemataProcessCompensation>> ReadBindingsAsync(string process) {
        using var scope      = _fixture.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcessCompensation>>();
        var rows = new List<SchemataProcessCompensation>();
        await foreach (var row in repository.ListAsync<SchemataProcessCompensation>(
                           query => query.Where(binding => binding.Process == process))) {
            rows.Add(row);
        }

        return rows;
    }
}