using System.Threading.Tasks;
using Schemata.Entity.Repository.Integration.Tests.Fixtures;
using Schemata.Flow.Skeleton.Entities;
using Xunit;

namespace Schemata.Entity.Repository.Integration.Tests;

[Trait("Category", "Integration")]
public class CanonicalNamePopulationShould : IAsyncLifetime
{
    private readonly RepositoryFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Add_SchemataProcess_PopulatesCanonicalNameFromCollectionAndName() {
        var entity = new SchemataProcess {
            DefinitionName = "test",
        };

        var (repository, scope) = _fixture.CreateScope<SchemataProcess>();
        using (scope) {
            await repository.AddAsync(entity);
            await repository.CommitAsync();
        }

        Assert.StartsWith("consumer-", entity.Name);
        Assert.Equal($"processes/{entity.Name}", entity.CanonicalName);
    }
}