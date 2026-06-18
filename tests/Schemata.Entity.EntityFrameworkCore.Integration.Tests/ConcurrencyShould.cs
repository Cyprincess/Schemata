using System;
using System.Linq;
using System.Threading.Tasks;
using Schemata.Abstractions.Exceptions;
using Schemata.Common;
using Schemata.Entity.EntityFrameworkCore.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Entity.EntityFrameworkCore.Integration.Tests;

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
                original        = entity!.Timestamp;
                entity.FullName = "updated";
                await repository.UpdateAsync(entity);
                await repository.CommitAsync();
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await LoadAsync(repository, id);
                Assert.Equal("updated", entity!.FullName);
                Assert.NotEqual(original, entity.Timestamp);
                Assert.NotEqual(Guid.Empty, entity.Timestamp);
            }
        }
    }

    [Fact]
    public async Task ConcurrentUpdateSameToken_OneAborts_NoLostUpdate() {
        var id = await SeedAsync("concurrent");

        var (repoA, scopeA) = _fixture.CreateScopeWithRepository();
        var (repoB, scopeB) = _fixture.CreateScopeWithRepository();

        using (scopeA)
        using (scopeB) {
            var entityA = await LoadAsync(repoA, id);
            var entityB = await LoadAsync(repoB, id);

            entityA!.FullName = "winner";
            entityB!.FullName = "loser";

            // Both stage their write while the row still carries the original token.
            await repoA.UpdateAsync(entityA);
            await repoB.UpdateAsync(entityB);

            await repoA.CommitAsync();

            await Assert.ThrowsAsync<ConcurrencyException>(() => repoB.CommitAsync());
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await LoadAsync(repository, id);
                Assert.Equal("winner", entity!.FullName);
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
                stale = entity!.Timestamp;
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await LoadAsync(repository, id);
                entity!.FullName = "advanced";
                await repository.UpdateAsync(entity);
                await repository.CommitAsync();
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await LoadAsync(repository, id);
                entity!.Timestamp = stale;
                entity.FullName   = "rejected";
                await repository.UpdateAsync(entity);
                await Assert.ThrowsAsync<ConcurrencyException>(() => repository.CommitAsync());
            }
        }
    }

    private async Task<Guid> SeedAsync(string name) {
        var id                  = Identifiers.NewUid();
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

    private static ValueTask<Student?> LoadAsync(Schemata.Entity.Repository.IRepository<Student> repository, Guid id) {
        return repository.FirstOrDefaultAsync<Student>(q => q.Where(s => s.Uid == id));
    }
}
