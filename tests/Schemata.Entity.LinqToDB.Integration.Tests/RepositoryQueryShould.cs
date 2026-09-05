using System;
using System.Linq;
using System.Threading.Tasks;
using Schemata.Entity.LinqToDB.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Entity.LinqToDB.Integration.Tests;

[Trait("Category", "Integration")]
public class RepositoryQueryShould : IAsyncLifetime
{
    private readonly IntegrationFixture _fixture = new();

    #region IAsyncLifetime Members

    public async Task InitializeAsync() {
        await _fixture.InitializeAsync();

        var (repository, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            await repository.AddAsync(new() {
                                          Uid      = Guid.NewGuid(),
                                          FullName = "Alice",
                                          Age      = 18,
                                          Grade    = 1,
                                          Name     = "q-alice",
                                      });
            await repository.CommitAsync();
        }

        {
            var (repo2, scope2) = _fixture.CreateScopeWithRepository();
            using (scope2) {
                await repo2.AddAsync(new() {
                                         Uid      = Guid.NewGuid(),
                                         FullName = "Bob",
                                         Age      = 19,
                                         Grade    = 2,
                                         Name     = "q-bob",
                                     });
                await repo2.CommitAsync();
            }
        }

        {
            var (repo3, scope3) = _fixture.CreateScopeWithRepository();
            using (scope3) {
                await repo3.AddAsync(new() {
                                         Uid      = Guid.NewGuid(),
                                         FullName = "Charlie",
                                         Age      = 20,
                                         Grade    = 2,
                                         Name     = "q-charlie",
                                     });
                await repo3.CommitAsync();
            }
        }
    }

    public Task DisposeAsync() { return _fixture.DisposeAsync(); }

    #endregion

    [Fact]
    public async Task EstimateCountAsync_OnSqlite_ReturnsExactFallbackCount() {
        var (repository, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            var count = await repository.EstimateCountAsync(q => q.Where(student => student.Grade == 2));

            Assert.Equal(2L, count);
        }
    }
}
