using System.Linq;
using System.Threading.Tasks;
using Schemata.Common;
using Schemata.Entity.LinqToDB.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Entity.LinqToDB.Integration.Tests;

[Trait("Category", "Integration")]
public class RepositoryTransactionShould : IAsyncLifetime
{
    private readonly IntegrationFixture _fixture = new();

    #region IAsyncLifetime Members

    public Task InitializeAsync() { return _fixture.InitializeAsync(); }

    public Task DisposeAsync() { return _fixture.DisposeAsync(); }

    #endregion

    [Fact]
    public async Task CommitAsync_CommitsTransaction() {
        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                await repository.AddAsync(new() {
                                              Uid      = Identifiers.NewUid(),
                                              FullName = "Tx-Alice",
                                              Age      = 18,
                                              Grade    = 1,
                                              Name     = "tx-alice",
                                          });
                await repository.CommitAsync();
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var found = await repository.FirstOrDefaultAsync(q => q.Where(s => s.Name == "tx-alice"));
                Assert.NotNull(found);
                Assert.Equal("Tx-Alice", found.FullName);
            }
        }
    }

    [Fact]
    public async Task Dispose_WithoutCommit_RollsBack() {
        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                await repository.AddAsync(new() {
                                              Uid      = Identifiers.NewUid(),
                                              FullName = "Tx-Rollback",
                                              Age      = 25,
                                              Grade    = 5,
                                              Name     = "tx-rollback",
                                          });
                // Scope disposal rolls back the pending transaction.
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var found = await repository.FirstOrDefaultAsync(q => q.Where(s => s.Name == "tx-rollback"));
                Assert.Null(found);
            }
        }
    }

    [Fact]
    public async Task CommitAsync_NoOperations_IsNoOp() {
        var (repository, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            // An empty repository scope commits successfully.
            var exception = await Record.ExceptionAsync(() => repository.CommitAsync());

            Assert.Null(exception);
        }
    }
}
