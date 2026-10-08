using System;
using System.Linq;
using System.Threading;
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
            services, () => new(new DataOptions().UseSQLite("Data Source=:memory:").UseMappingSchema(schema)));
        Assert.Null(memory.GetQueryCacheKey(Enumerable.Empty<Student>().AsQueryable()));

        using var named = new LinqToDbRepository<TestDataConnection, Student>(
            services, () => new(new DataOptions().UseSQLite("Data Source=namedmem;Mode=Memory").UseMappingSchema(schema)));
        Assert.Null(named.GetQueryCacheKey(Enumerable.Empty<Student>().AsQueryable()));
    }

    [Trait("Layer", "Integration")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Joined_ReadOnly_Repository_Bypasses_Warm_Reads_And_Cold_Fills(bool commit) {
        await SeedAsync();
        Assert.Equal("Original", await ReadNameAsync());

        using var scope = _fixture.ServiceProvider.CreateScope();
        using var writer = scope.ServiceProvider.GetRequiredService<IRepository<Student>>();
        using var reader = scope.ServiceProvider.GetRequiredService<IRepository<Student>>();
        using var uow = writer.Begin();
        reader.Join(uow);
        var student = (await writer.FirstOrDefaultAsync<Student>(null))!;
        student.FullName = "Transaction";
        student.Age = 20;
        Assert.Equal(MutationResult.Applied, await writer.UpdateAsync(student));

        Assert.Equal("Transaction", await reader.FirstOrDefaultAsync(q => q.Select(s => s.FullName)));
        Assert.Equal(20, await reader.FirstOrDefaultAsync(q => q.Select(s => s.Age)));

        string? callbackValue = null;
        uow.AddCommitSink(CommitOrders.Resource, async ct => callbackValue = await ReadNameAsync(ct));
        if (commit) {
            await uow.CommitAsync();
            Assert.Equal("Transaction", callbackValue);
        } else {
            await uow.RollbackAsync();
            Assert.Null(callbackValue);
            var factory = _fixture.ServiceProvider.GetRequiredService<Func<TestDataConnection>>();
            await using var independent = factory();
            await independent.Students.Set(s => s.FullName, "Uncached SQL").UpdateAsync();
        }

        Assert.Equal(commit ? "Transaction" : "Original", await ReadNameAsync());
        using var freshScope = _fixture.ServiceProvider.CreateScope();
        using var fresh = freshScope.ServiceProvider.GetRequiredService<IRepository<Student>>();
        Assert.Equal(commit ? 20 : 10, await fresh.FirstOrDefaultAsync(q => q.Select(s => s.Age)));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await reader.FirstOrDefaultAsync(q => q.Select(s => s.FullName)));
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task Joined_Query_After_Disposal_Cannot_Serve_Shared_Cache() {
        await SeedAsync();
        Assert.Equal("Original", await ReadNameAsync());
        using var scope = _fixture.ServiceProvider.CreateScope();
        using var reader = scope.ServiceProvider.GetRequiredService<IRepository<Student>>();
        var uow = reader.Begin();
        uow.Dispose();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await reader.FirstOrDefaultAsync(q => q.Select(s => s.FullName)));
        Assert.Equal("Original", await ReadNameAsync());
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task Joined_CrossEntity_Projection_Reads_Transactional_Rows_And_Rollback_Restores_Them() {
        await SeedAsync();
        using var scope = _fixture.ServiceProvider.CreateScope();
        using var courses = scope.ServiceProvider.GetRequiredService<IRepository<Course>>();
        await courses.AddAsync(new() { Uid = Guid.NewGuid(), Name = "course", Title = "Original course", Credits = 3 });
        await courses.CommitAsync();
        var factory = _fixture.ServiceProvider.GetRequiredService<Func<TestDataConnection>>();
        using (var preload = new AssociationRepository(scope.ServiceProvider, factory)) {
            Assert.Equal("Original course", await preload.ReadCourseTitleAsync());
        }

        using var students = new AssociationRepository(scope.ServiceProvider, factory);
        using var uow = courses.Begin();
        students.Join(uow);
        var course = (await courses.FirstOrDefaultAsync<Course>(null))!;
        course.Title = "Transaction course";
        await courses.UpdateAsync(course);
        Assert.Equal("Transaction course", await students.ReadCourseTitleAsync());
        await uow.RollbackAsync();

        using var independent = _fixture.ServiceProvider.CreateScope();
        using var freshCourses = independent.ServiceProvider.GetRequiredService<IRepository<Course>>();
        Assert.Equal("Original course", (await freshCourses.FirstOrDefaultAsync<Course>(null))?.Title);
    }

    private async Task SeedAsync() {
        using var scope = _fixture.ServiceProvider.CreateScope();
        using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Student>>();
        await repository.AddAsync(new() { Uid = Guid.NewGuid(), Name = "student", FullName = "Original", Age = 10 });
        await repository.CommitAsync();
    }

    private async Task<string?> ReadNameAsync(CancellationToken ct = default) {
        using var scope = _fixture.ServiceProvider.CreateScope();
        using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Student>>();
        return await repository.FirstOrDefaultAsync(q => q.Select(s => s.FullName), ct);
    }

    private sealed class AssociationRepository(IServiceProvider services, Func<TestDataConnection> factory)
        : LinqToDbRepository<TestDataConnection, Student>(services, factory)
    {
        public ValueTask<string?> ReadCourseTitleAsync() {
            var courses = Context.Courses;
            return FirstOrDefaultAsync(q => q.Select(student => courses.Select(course => course.Title).FirstOrDefault()));
        }
    }
}
