using System.Linq;
using System.Threading.Tasks;
using Schemata.Entity.EntityFrameworkCore.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Entity.EntityFrameworkCore.Integration.Tests;

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
        long id;
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
                await repository.CommitAsync();
                id = entity.Id;
                Assert.NotEqual(0, id);
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var found = await repository.FindAsync([id]);
                Assert.NotNull(found);
                Assert.Equal("Alice", found!.FullName);
            }
        }
    }

    [Fact]
    public async Task Update_ThenCommit_ModifiesEntity() {
        long id;
        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = new Student {
                    FullName = "Bob",
                    Age      = 19,
                    Grade    = 2,
                    Name     = "bob",
                };
                await repository.AddAsync(entity);
                await repository.CommitAsync();
                id = entity.Id;
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await repository.FindAsync([id]);
                Assert.NotNull(entity);
                entity!.FullName = "Bob Updated";
                await repository.UpdateAsync(entity);
                await repository.CommitAsync();
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await repository.FindAsync([id]);
                Assert.Equal("Bob Updated", entity?.FullName);
            }
        }
    }

    [Fact]
    public async Task Remove_ThenCommit_DeletesEntity() {
        long id;
        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = new Student {
                    FullName = "Charlie",
                    Age      = 20,
                    Grade    = 3,
                    Name     = "charlie",
                };
                await repository.AddAsync(entity);
                await repository.CommitAsync();
                id = entity.Id;
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await repository.FindAsync([id]);
                Assert.NotNull(entity);
                await repository.RemoveAsync(entity!);
                await repository.CommitAsync();
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await repository.FindAsync([id]);
                Assert.Null(entity);
            }
        }
    }

    [Fact]
    public async Task Add_WithoutCommit_NotVisibleInNewScope() {
        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = new Student {
                    FullName = "Dave",
                    Age      = 21,
                    Grade    = 4,
                    Name     = "dave",
                };
                await repository.AddAsync(entity);
                // intentionally no CommitAsync
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var found = await repository.FirstOrDefaultAsync(q => q.Where(s => s.FullName == "Dave"));
                Assert.Null(found);
            }
        }
    }
}
