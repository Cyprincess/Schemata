using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Schemata.Entity.EntityFrameworkCore.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Entity.EntityFrameworkCore.Integration.Tests;

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
                                          FullName = "Alice",
                                          Age      = 18,
                                          Grade    = 1,
                                          Name     = "alice",
            });
            await repository.AddAsync(new() {
                                          FullName = "Bob",
                                          Age      = 19,
                                          Grade    = 2,
                                          Name     = "bob",
            });
            await repository.AddAsync(new() {
                                          FullName = "Charlie",
                                          Age      = 20,
                                          Grade    = 2,
                                          Name     = "charlie",
            });
            await repository.CommitAsync();
        }
    }

    public Task DisposeAsync() { return _fixture.DisposeAsync(); }

    #endregion

    [Fact]
    public async Task ListAsync_WithPredicate_ReturnsMatchingEntities() {
        var (repository, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            var results = new List<Student>();
            await foreach (var student in repository.ListAsync(q => q.Where(s => s.Grade == 2))) {
                results.Add(student);
            }

            Assert.Equal(2, results.Count);
            Assert.All(results, s => Assert.Equal(2, s.Grade));
        }
    }

    [Fact]
    public async Task FirstOrDefaultAsync_Found_ReturnsEntity() {
        var (repository, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            var found = await repository.FirstOrDefaultAsync(q => q.Where(s => s.FullName == "Alice"));

            Assert.NotNull(found);
            Assert.Equal("Alice", found!.FullName);
        }
    }

    [Fact]
    public async Task FirstOrDefaultAsync_NotFound_ReturnsNull() {
        var (repository, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            var found = await repository.FirstOrDefaultAsync(q => q.Where(s => s.FullName == "Nonexistent"));

            Assert.Null(found);
        }
    }

    [Fact]
    public async Task SingleOrDefaultAsync_Found_ReturnsEntity() {
        var (repository, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            var found = await repository.SingleOrDefaultAsync(q => q.Where(s => s.FullName == "Bob"));

            Assert.NotNull(found);
            Assert.Equal("Bob", found!.FullName);
        }
    }

    [Fact]
    public async Task AnyAsync_WithMatch_ReturnsTrue() {
        var (repository, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            var exists = await repository.AnyAsync(q => q.Where(s => s.Age >= 18));

            Assert.True(exists);
        }
    }

    [Fact]
    public async Task AnyAsync_WithNoMatch_ReturnsFalse() {
        var (repository, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            var exists = await repository.AnyAsync(q => q.Where(s => s.Age > 100));

            Assert.False(exists);
        }
    }

    [Fact]
    public async Task CountAsync_ReturnsCorrectCount() {
        var (repository, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            var count = await repository.CountAsync(q => q.Where(s => s.Grade == 2));

            Assert.Equal(2, count);
        }
    }

    [Fact]
    public async Task LongCountAsync_ReturnsCorrectCount() {
        var (repository, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            var count = await repository.LongCountAsync<Student>(null);

            Assert.Equal(3L, count);
        }
    }
}
