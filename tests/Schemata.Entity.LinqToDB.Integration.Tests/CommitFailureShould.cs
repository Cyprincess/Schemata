using System;
using System.Linq;
using System.Threading.Tasks;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.LinqToDB.Integration.Tests.Fixtures;
using Schemata.Entity.Repository.Advisors;
using Xunit;
using Schemata.Entity.Repository;

namespace Schemata.Entity.LinqToDB.Integration.Tests;

[Trait("Category", "Integration")]
public class CommitFailureShould : IAsyncLifetime
{
    private readonly IntegrationFixture _fixture = new();

    #region IAsyncLifetime Members

    public Task InitializeAsync() { return _fixture.InitializeAsync(); }

    public Task DisposeAsync() { return _fixture.DisposeAsync(); }

    #endregion

    [Fact]
    public async Task FailedInsert_RollsBackTransaction() {
        var existing = await SeedAsync("existing");

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                // LinqToDB executes inserts immediately, so the pre-check-bypassed duplicate fails
                // against the database during AddAsync; the actual SQL boundary classifies the
                // provider's unique violation as a consumer-facing ALREADY_EXISTS error.
                await Assert.ThrowsAsync<AlreadyExistsException>(() => repository.AddAsync(new() {
                    Uid      = existing,
                    FullName = "duplicate",
                    Name     = "duplicate",
                    Age      = 1,
                    Grade    = 1,
                }, default, new MutationOptions(IgnoreUniqueness: true)));

                // Scope disposal rolls back the implicit unit of work's transaction.
            }
        }

        {
            var (verifier, verifyScope) = _fixture.CreateScopeWithRepository();
            using (verifyScope) {
                var duplicates = await verifier.CountAsync<Student>(q => q.Where(s => s.Uid == existing));
                Assert.Equal(1, duplicates);
            }
        }
    }

    private async Task<Guid> SeedAsync(string name) {
        var id = Guid.NewGuid();
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
