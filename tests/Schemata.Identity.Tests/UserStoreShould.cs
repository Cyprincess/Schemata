using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Stores;
using Xunit;

namespace Schemata.Identity.Tests;

public class UserStoreShould
{
    [Fact]
    public async Task DeleteUser_RemovesAllDependentRowsAtomically() {
        var user  = new SchemataUser { Uid         = Identifiers.NewUid() };
        var link  = new SchemataUserRole { UserId  = user.Uid, RoleId = Identifiers.NewUid() };
        var claim = new SchemataUserClaim { UserId = user.Uid };
        var login = new SchemataUserLogin { UserId = user.Uid, LoginProvider = "google", ProviderKey = "k" };
        var token = new SchemataUserToken { UserId = user.Uid, LoginProvider = "google", Name        = "refresh" };

        var users    = new Mock<IRepository<SchemataUser>>();
        var roles    = new Mock<IRepository<SchemataRole>>();
        var claims   = new Mock<IRepository<SchemataUserClaim>>();
        var userRole = new Mock<IRepository<SchemataUserRole>>();
        var logins   = new Mock<IRepository<SchemataUserLogin>>();
        var tokens   = new Mock<IRepository<SchemataUserToken>>();

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(u => u.DisposeAsync()).Returns(ValueTask.CompletedTask);

        users.Setup(r => r.Begin()).Returns(uow.Object);
        users.Setup(r => r.RemoveAsync(It.IsAny<SchemataUser>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        userRole.Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<SchemataUserRole>, IQueryable<SchemataUserRole>>?>(),
                                        It.IsAny<CancellationToken>()))
                .Returns(OneAsync(link));
        userRole.Setup(r => r.RemoveAsync(It.IsAny<SchemataUserRole>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        claims.Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<SchemataUserClaim>, IQueryable<SchemataUserClaim>>?>(),
                                      It.IsAny<CancellationToken>()))
              .Returns(OneAsync(claim));
        claims.Setup(r => r.RemoveAsync(It.IsAny<SchemataUserClaim>(), It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);
        logins.Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<SchemataUserLogin>, IQueryable<SchemataUserLogin>>?>(),
                                      It.IsAny<CancellationToken>()))
              .Returns(OneAsync(login));
        logins.Setup(r => r.RemoveAsync(It.IsAny<SchemataUserLogin>(), It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);
        tokens.Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<SchemataUserToken>, IQueryable<SchemataUserToken>>?>(),
                                      It.IsAny<CancellationToken>()))
              .Returns(OneAsync(token));
        tokens.Setup(r => r.RemoveAsync(It.IsAny<SchemataUserToken>(), It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

        var store = new SchemataUserStore<SchemataUser>(users.Object, roles.Object, claims.Object, userRole.Object,
                                                        logins.Object, tokens.Object);

        var result = await store.DeleteAsync(user, CancellationToken.None);

        Assert.True(result.Succeeded);

        // Every dependent repository enlists in the user's unit of work before child rows are removed.
        userRole.Verify(r => r.Join(uow.Object), Times.Once);
        claims.Verify(r => r.Join(uow.Object), Times.Once);
        logins.Verify(r => r.Join(uow.Object), Times.Once);
        tokens.Verify(r => r.Join(uow.Object), Times.Once);
        userRole.Verify(r => r.RemoveAsync(link, It.IsAny<CancellationToken>()), Times.Once);
        claims.Verify(r => r.RemoveAsync(claim, It.IsAny<CancellationToken>()), Times.Once);
        logins.Verify(r => r.RemoveAsync(login, It.IsAny<CancellationToken>()), Times.Once);
        tokens.Verify(r => r.RemoveAsync(token, It.IsAny<CancellationToken>()), Times.Once);
        users.Verify(r => r.RemoveAsync(user, It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        userRole.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        users.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static async IAsyncEnumerable<T> OneAsync<T>(T item) {
        yield return item;
        await Task.CompletedTask;
    }
}
