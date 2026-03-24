using System.Linq;
using System.Threading.Tasks;
using Schemata.Entity.LinqToDB.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Entity.LinqToDB.Integration.Tests;

[Trait("Category", "Integration")]
public class RepositoryCrudShould : IAsyncLifetime
{
    private readonly IntegrationFixture _fixture = new();

    #region IAsyncLifetime Members

    public Task InitializeAsync() { return _fixture.InitializeAsync(); }

    public Task DisposeAsync() { return _fixture.DisposeAsync(); }

    #endregion

    [Fact]
    public async Task Add_ThenCommit_PersistsEntity() {
        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = new Student {
                    FullName = "Alice",
                    Age      = 18,
                    Grade    = 1,
                    Name     = "alice",
                };
                await repository.AddAsync(entity);
                var rows = await repository.CommitAsync();
                Assert.True(rows > 0);
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var found = await repository.FirstOrDefaultAsync(q => q.Where(s => s.FullName == "Alice"));
                Assert.NotNull(found);
                Assert.Equal("Alice", found.FullName);
            }
        }
    }

    [Fact]
    public async Task Update_ThenCommit_ModifiesEntity() {
        Student original;
        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                original = new() {
                    FullName = "Bob",
                    Age      = 19,
                    Grade    = 2,
                    Name     = "bob-update",
                };
                await repository.AddAsync(original);
                await repository.CommitAsync();
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await repository.FirstOrDefaultAsync(q => q.Where(s => s.Name == "bob-update"));
                Assert.NotNull(entity);
                entity.FullName = "Bob Updated";
                await repository.UpdateAsync(entity);
                await repository.CommitAsync();
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await repository.FirstOrDefaultAsync(q => q.Where(s => s.Name == "bob-update"));
                Assert.Equal("Bob Updated", entity?.FullName);
            }
        }
    }

    [Fact]
    public async Task Remove_ThenCommit_DeletesEntity() {
        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = new Student {
                    FullName = "Charlie",
                    Age      = 20,
                    Grade    = 3,
                    Name     = "charlie-rm",
                };
                await repository.AddAsync(entity);
                await repository.CommitAsync();
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await repository.FirstOrDefaultAsync(q => q.Where(s => s.Name == "charlie-rm"));
                Assert.NotNull(entity);
                await repository.RemoveAsync(entity);
                await repository.CommitAsync();
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await repository.FirstOrDefaultAsync(q => q.Where(s => s.Name == "charlie-rm"));
                Assert.Null(entity);
            }
        }
    }
}
