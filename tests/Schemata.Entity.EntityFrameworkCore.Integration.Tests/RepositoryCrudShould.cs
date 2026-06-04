using System;
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
        Guid uid;
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
                uid = entity.Uid;
                Assert.NotEqual(Guid.Empty, uid);
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var found = await repository.FindAsync([uid]);
                Assert.NotNull(found);
                Assert.Equal("Alice", found.FullName);
            }
        }
    }

    [Fact]
    public async Task Update_ThenCommit_ModifiesEntity() {
        Guid uid;
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
                uid = entity.Uid;
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await repository.FindAsync([uid]);
                Assert.NotNull(entity);
                entity.FullName = "Bob Updated";
                await repository.UpdateAsync(entity);
                await repository.CommitAsync();
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await repository.FindAsync([uid]);
                Assert.Equal("Bob Updated", entity?.FullName);
            }
        }
    }

    [Fact]
    public async Task Remove_ThenCommit_DeletesEntity() {
        Guid uid;
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
                uid = entity.Uid;
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await repository.FindAsync([uid]);
                Assert.NotNull(entity);
                await repository.RemoveAsync(entity);
                await repository.CommitAsync();
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await repository.FindAsync([uid]);
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
