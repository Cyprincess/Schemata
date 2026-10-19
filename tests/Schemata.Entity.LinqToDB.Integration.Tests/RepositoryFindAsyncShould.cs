using System.Threading.Tasks;
using Schemata.Entity.LinqToDB.Integration.Tests.Fixtures;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Entity.LinqToDB.Integration.Tests;

[Trait("Category", "Integration")]
public class RepositoryFindAsyncShould : IAsyncLifetime
{
    private readonly IntegrationFixture _fixture = new();

    #region IAsyncLifetime Members

    public Task InitializeAsync() { return _fixture.InitializeAsync(); }

    public Task DisposeAsync() { return _fixture.DisposeAsync(); }

    #endregion

    [Fact]
    public async Task FindAsync_NullableKey_MatchesRowByValue() {
        {
            var (repository, scope) = _fixture.CreateScopeWithNullableKeyThingRepository();
            using (scope) {
                await repository.AddAsync(new NullableKeyThing { Id = 42, Name = "answer" });
                await repository.CommitAsync();
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithNullableKeyThingRepository();
            using (scope) {
                var found = await repository.FindAsync([42L]);

                Assert.NotNull(found);
                Assert.Equal("answer", found.Name);
            }
        }
    }
}
