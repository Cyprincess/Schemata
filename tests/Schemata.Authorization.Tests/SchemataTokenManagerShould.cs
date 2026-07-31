using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Authorization.Foundation.Managers;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Entity.Repository;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Tests;

public class SchemataTokenManagerShould
{
    [Fact]
    public async Task Create_Twice_Through_One_Injected_Repository() {
        var (manager, repository) = NewManager();

        await manager.CreateAsync(new SchemataToken { Name = "access" });
        await manager.CreateAsync(new SchemataToken { Name = "refresh" });

        repository.Verify(r => r.AddAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Update_After_Create_Through_One_Injected_Repository() {
        var (manager, repository) = NewManager();
        var token = new SchemataToken { Name = "access" };

        await manager.CreateAsync(token);
        await manager.UpdateAsync(token);

        repository.Verify(r => r.UpdateAsync(token, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RevokeByAuthorization_Lists_Updates_And_Commits_Once() {
        var token = new SchemataToken {
            Name = "access", Authorization = "auth-1", Status = TokenStatuses.Valid,
        };
        var (manager, repository) = NewManager(r =>
            r.Setup(x => x.ListAsync(
                        It.IsAny<Func<IQueryable<SchemataToken>, IQueryable<SchemataToken>>>(),
                        It.IsAny<CancellationToken>()))
             .Returns((Func<IQueryable<SchemataToken>, IQueryable<SchemataToken>> predicate, CancellationToken _) =>
                 EnumerateAsync(predicate(new[] { token }.AsQueryable()))));

        var count = await manager.RevokeByAuthorizationAsync("auth-1");

        Assert.Equal(1, count);
        Assert.Equal(TokenStatuses.Revoked, token.Status);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static (SchemataTokenManager<SchemataToken> Manager, Mock<IRepository<SchemataToken>> Repository) NewManager(
        Action<Mock<IRepository<SchemataToken>>>? configure = null) {
        var repository = new Mock<IRepository<SchemataToken>>();
        configure?.Invoke(repository);

        return (new(repository.Object), repository);
    }

    private static async IAsyncEnumerable<T> EnumerateAsync<T>(IEnumerable<T> items) {
        foreach (var item in items) {
            yield return item;
        }

        await Task.CompletedTask;
    }
}
