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

        var compensated = await CompleteAsync(process);

        var transition = Assert.Single(compensated.Transitions, current => current.Kind == TransitionKind.Compensate);
        Assert.Equal("host", transition.Previous);
        Assert.Equal("undo-host", transition.Posterior);
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
        return await runner.CompleteAsync(persisted!, null, null, CancellationToken.None);
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

[Trait("Category", "Integration")]
public sealed class EfCoreCompensationPersistenceShould : CompensationPersistenceShould, IClassFixture<EfCoreFlowFixture>
{
    public EfCoreCompensationPersistenceShould(EfCoreFlowFixture fixture) : base(fixture) { }
}

[Trait("Category", "Integration")]
public sealed class LinqToDbCompensationPersistenceShould : CompensationPersistenceShould, IClassFixture<LinqToDbFlowFixture>
{
    public LinqToDbCompensationPersistenceShould(LinqToDbFlowFixture fixture) : base(fixture) { }
}
