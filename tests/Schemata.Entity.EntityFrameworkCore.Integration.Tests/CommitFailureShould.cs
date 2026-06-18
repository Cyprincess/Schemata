using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Schemata.Common;
using Schemata.Entity.EntityFrameworkCore.Integration.Tests.Fixtures;
using Schemata.Entity.Repository.Advisors;
using Xunit;

namespace Schemata.Entity.EntityFrameworkCore.Integration.Tests;

[Trait("Category", "Integration")]
public class CommitFailureShould : IAsyncLifetime
{
    private readonly IntegrationFixture _fixture = new();

    #region IAsyncLifetime Members

    public Task InitializeAsync() { return _fixture.InitializeAsync(); }

    public Task DisposeAsync() { return _fixture.DisposeAsync(); }

    #endregion

    [Fact]
    public async Task CommitFailure_RollsBackPendingInsert() {
        var existing = await SeedAsync("existing");

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                // Suppress the optimistic pre-check so the duplicate primary key reaches the database;
                // EF Core buffers the insert and the collision surfaces only at commit time.
                using (repository.AdviceContext.Use<UniquenessSuppressed>()) {
                    await repository.AddAsync(new() {
                        Uid = existing, FullName = "duplicate", Name = "duplicate", Age = 1, Grade = 1,
                    });
                }

                await Assert.ThrowsAsync<DbUpdateException>(() => repository.CommitAsync());
            }
        }

        // The failed commit must roll back its transaction: a fresh scope sees only the seeded row.
        {
            var (verifier, verifyScope) = _fixture.CreateScopeWithRepository();
            using (verifyScope) {
                var count = await verifier.CountAsync<Student>(q => q.Where(s => s.Uid == existing));
                Assert.Equal(1, count);
            }
        }
    }

    private async Task<Guid> SeedAsync(string name) {
        var id                  = Identifiers.NewUid();
        var (repository, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            await repository.AddAsync(new() {
                Uid      = id,
                FullName = name,
                Name     = name,
                Age      = 20,
                Grade    = 1,
            });
            await repository.CommitAsync();
        }

        return id;
    }
}
