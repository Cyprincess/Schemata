using System;
using System.Linq;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Mapping;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.LinqToDB.Integration.Tests.Fixtures;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Xunit;
namespace Schemata.Entity.LinqToDB.Integration.Tests;

[Trait("Category", "Integration")]
public class QueryCacheUnitOfWorkShould : IAsyncLifetime
{
    private readonly IntegrationFixture _fixture = new(useQueryCache: true);

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task AddQueryRollback_DoesNotLeavePhantomCacheEntry() {
        var (repository, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            repository.AdviceContext.Set(new UniquenessSuppressed());
            await repository.AddAsync(new() { Uid = Guid.NewGuid(), Name = "Pending", FullName = "Pending" });

            Assert.NotNull(await repository.FirstOrDefaultAsync<Student>(null));
        }

        // A phantom pre-commit cache entry would resurrect the rolled-back row here.
        var (fresh, freshScope) = _fixture.CreateScopeWithRepository();
        using (freshScope) {
            Assert.Null(await fresh.FirstOrDefaultAsync<Student>(null));
        }
    }

    [Fact]
    public async Task CommitThenQuery_PopulatesAndServesCache() {
        var (repository, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            repository.AdviceContext.Set(new UniquenessSuppressed());
            await repository.AddAsync(new() { Uid = Guid.NewGuid(), Name = "Committed", FullName = "Committed" });
            await repository.CommitAsync();
        }

        var (first, firstScope) = _fixture.CreateScopeWithRepository();
        using (firstScope) {
            Assert.Equal("Committed", (await first.FirstOrDefaultAsync<Student>(null))?.FullName);
        }

        // Mutate the row underneath the repository so no eviction advisor fires; a cache
        // hit on the next query serves the stale value, a miss would observe "Mutated".
        var factory = _fixture.ServiceProvider.GetRequiredService<Func<TestDataConnection>>();
        await using (var connection = factory()) {
            await connection.Students
                            .Where(student => student.FullName == "Committed")
                            .Set(student => student.FullName, "Mutated")
                            .UpdateAsync();
        }

        var (second, secondScope) = _fixture.CreateScopeWithRepository();
        using (secondScope) {
            Assert.Equal("Committed", (await second.FirstOrDefaultAsync<Student>(null))?.FullName);
        }
    }

    [Fact]
    public async Task QueryCacheKey_Is_Stable_And_Distinguishes_Predicates() {
        var (repository, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            var provider = (IQueryCacheKeyProvider)repository;

            var factory = _fixture.ServiceProvider.GetRequiredService<Func<TestDataConnection>>();
            await using var connection = factory();

            var keyed = connection.Students.Where(student => student.FullName == "Committed");

            var key = provider.GetQueryCacheKey(keyed);

            Assert.NotNull(key);
            Assert.Equal(key, provider.GetQueryCacheKey(keyed));
            Assert.NotEqual(key, provider.GetQueryCacheKey(connection.Students));
        }
    }

    [Fact]
    public void QueryCacheKey_Is_Null_For_InMemory_Connections() {
        using var services = new ServiceCollection().BuildServiceProvider();

        var schema = new MappingSchema();
        schema.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());

        using var memory = new LinqToDbRepository<TestDataConnection, Student>(
            services, () => new TestDataConnection(new DataOptions().UseSQLite("Data Source=:memory:").UseMappingSchema(schema)));
        Assert.Null(memory.GetQueryCacheKey(Enumerable.Empty<Student>().AsQueryable()));

        using var named = new LinqToDbRepository<TestDataConnection, Student>(
            services, () => new TestDataConnection(new DataOptions().UseSQLite("Data Source=namedmem;Mode=Memory").UseMappingSchema(schema)));
        Assert.Null(named.GetQueryCacheKey(Enumerable.Empty<Student>().AsQueryable()));
    }
}
