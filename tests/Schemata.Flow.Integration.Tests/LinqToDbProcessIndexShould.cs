using System;
using System.Linq;
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
    public async Task DirectWrites_CanShareAnIdempotencyKey_WhileTheOwnerGuardsIt() {
        var key = Guid.NewGuid().ToString("n");
        using (var scope = _fixture.CreateScope()) {
            var processes = scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcess>>();
            await processes.AddAsync(Process(key));
            await processes.AddAsync(Process(key));
            await processes.CommitAsync();
        }

        using var verifyScope = _fixture.CreateScope();
        var       verify      = verifyScope.ServiceProvider.GetRequiredService<IRepository<SchemataProcess>>();
        Assert.Equal(2, await verify.CountAsync(query => query.Where(row =>
            row.DefinitionName == nameof(IdempotencyProcess) && row.IdempotencyKey == key)));
    }

    private static SchemataProcess Process(string key) {
        var name = Guid.NewGuid().ToString("n");
        return new() {
            Name              = name,
            CanonicalName     = $"processes/{name}",
            DefinitionName    = nameof(IdempotencyProcess),
            DefinitionVersion = "1",
            IdempotencyKey    = key,
            State             = "Waiting",
        };
    }
}