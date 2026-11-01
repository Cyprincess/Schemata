using System;
using System.Linq;
using System.Threading.Tasks;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.LinqToDB.Integration.Tests.Fixtures;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Entity.LinqToDB.Integration.Tests;

[Trait("Category", "Integration")]
public class ConcurrencyShould : IAsyncLifetime
{
    private readonly IntegrationFixture _fixture = new();

    #region IAsyncLifetime Members

    public Task InitializeAsync() { return _fixture.InitializeAsync(); }

    public Task DisposeAsync() { return _fixture.DisposeAsync(); }

    #endregion

    [Fact]
    public async Task FreshUpdate_BumpsTimestamp() {
        var id = await SeedAsync("fresh");

        Guid original;
        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await LoadAsync(repository, id);
                Assert.NotNull(entity);
                original        = entity.Timestamp;
                entity.FullName = "updated";
                await repository.UpdateAsync(entity);
                await repository.CommitAsync();
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await LoadAsync(repository, id);
                Assert.NotNull(entity);
                Assert.Equal("updated", entity.FullName);
                Assert.NotEqual(original, entity.Timestamp);
                Assert.NotEqual(Guid.Empty, entity.Timestamp);
            }
        }
    }

    [Fact]
    public async Task StaleToken_Aborts() {
        var id = await SeedAsync("stale");

        Guid stale;
        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await LoadAsync(repository, id);
                Assert.NotNull(entity);
                stale = entity.Timestamp;
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await LoadAsync(repository, id);
                Assert.NotNull(entity);
                entity.FullName = "advanced";
                await repository.UpdateAsync(entity);
                await repository.CommitAsync();
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await LoadAsync(repository, id);
                Assert.NotNull(entity);
                entity.Timestamp = stale;
                entity.FullName   = "rejected";
                await Assert.ThrowsAsync<AbortedException>(() => repository.UpdateAsync(entity));
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await LoadAsync(repository, id);
                Assert.NotNull(entity);
                Assert.Equal("advanced", entity.FullName);
            }
        }
    }

    [Fact]
    public async Task RepeatedUpdate_BeforeCommit_CommitsOnce() {
        var id = await SeedAsync("repeat");

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await LoadAsync(repository, id);
                Assert.NotNull(entity);
                entity.FullName = "first";
                await repository.UpdateAsync(entity);
                entity.FullName = "second";
                await repository.UpdateAsync(entity);
                await repository.CommitAsync();
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await LoadAsync(repository, id);
                Assert.NotNull(entity);
                Assert.Equal("second", entity.FullName);
            }
        }
    }

    [Fact]
    public async Task RepeatedUpdate_AcrossCommits_CommitsBoth() {
        var id = await SeedAsync("repeat-commit");

        Student? carried;
        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                carried = await LoadAsync(repository, id);
                Assert.NotNull(carried);
                carried.FullName = "first";
                await repository.UpdateAsync(carried);
                await repository.CommitAsync();
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                carried.FullName = "second";
                await repository.UpdateAsync(carried);
                await repository.CommitAsync();
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await LoadAsync(repository, id);
                Assert.NotNull(entity);
                Assert.Equal("second", entity.FullName);
            }
        }
    }

    [Fact]
    public async Task CommittedToken_MatchesReloadedRow() {
        var id = await SeedAsync("token");

        Guid inMemory;
        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await LoadAsync(repository, id);
                Assert.NotNull(entity);
                entity.FullName = "updated";
                await repository.UpdateAsync(entity);
                await repository.CommitAsync();
                inMemory = entity.Timestamp;
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await LoadAsync(repository, id);
                Assert.NotNull(entity);
                Assert.NotEqual(Guid.Empty, inMemory);
                Assert.Equal(inMemory, entity.Timestamp);
            }
        }
    }

    [Fact]
    public async Task StaleDelete_Aborts() {
        var id = await SeedAsync("stale-delete");

        Guid stale;
        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await LoadAsync(repository, id);
                Assert.NotNull(entity);
                stale = entity.Timestamp;
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await LoadAsync(repository, id);
                Assert.NotNull(entity);
                entity.FullName = "advanced";
                await repository.UpdateAsync(entity);
                await repository.CommitAsync();
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await LoadAsync(repository, id);
                Assert.NotNull(entity);
                entity.Timestamp = stale;
                await Assert.ThrowsAsync<AbortedException>(() => repository.RemoveAsync(entity));
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                Assert.NotNull(await LoadAsync(repository, id));
            }
        }
    }

    private async Task<Guid> SeedAsync(string name) {
        var id = Guid.NewGuid();
        var (repository, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            await repository.AddAsync(new() {
                                          Uid      = id,
                                          FullName = name,
                                          Name     = name,
                                          Age      = 20,
                                          Grade    = 1,
                                      });
            await repository.CommitAsync();
        }

        return id;
    }

    private static ValueTask<Student?> LoadAsync(IRepository<Student> repository, Guid id) {
        return repository.FirstOrDefaultAsync<Student>(q => q.Where(s => s.Uid == id));
    }
}
