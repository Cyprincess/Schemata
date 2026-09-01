using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.Repository;
using Schemata.Flow.Integration.Tests.Fixtures;
using Schemata.Flow.Skeleton.Entities;
using Xunit;

namespace Schemata.Flow.Integration.Tests;

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
        var       duplicate      = duplicateScope.ServiceProvider.GetRequiredService<IRepository<SchemataProcess>>();
        var       error          = await Assert.ThrowsAnyAsync<Exception>(() => duplicate.AddAsync(Process(key)));
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