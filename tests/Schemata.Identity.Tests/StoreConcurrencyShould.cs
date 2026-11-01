using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Moq;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Common.Errors;
using Schemata.Entity.Repository;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Stores;
using Xunit;

namespace Schemata.Identity.Tests;

[Trait("Layer", "Unit")]
public class StoreConcurrencyShould
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_Translates_Concurrency_Into_The_Identity_Error(bool role) {
        var userMutation = new Mock<IResourceMutation<SchemataUser>>();
        userMutation.Setup(m => m.UpdateAsync(It.IsAny<SchemataUser>(), null, Operations.Update, It.IsAny<CancellationToken>()))
                    .ThrowsAsync(SchemataResourceErrors.Aborted<SchemataUser>());
        var roleMutation = new Mock<IResourceMutation<SchemataRole>>();
        roleMutation.Setup(m => m.UpdateAsync(It.IsAny<SchemataRole>(), null, Operations.Update, It.IsAny<CancellationToken>()))
                    .ThrowsAsync(SchemataResourceErrors.Aborted<SchemataRole>());

        IdentityResult result;
        if (role) {
            result = await CreateRoleStore(roleMutation).UpdateAsync(new SchemataRole { Name = "r" });
        } else {
            result = await CreateUserStore(userMutation).UpdateAsync(new SchemataUser { Name = "u" });
        }

        Assert.False(result.Succeeded);
        var error = Assert.Single(result.Errors);
        Assert.Equal(nameof(IdentityErrorDescriber.ConcurrencyFailure), error.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delete_Translates_Concurrency_Into_The_Identity_Error(bool role) {
        var userMutation = new Mock<IResourceMutation<SchemataUser>>();
        userMutation.Setup(m => m.DeleteAsync(It.IsAny<SchemataUser>(), null, Operations.Delete, It.IsAny<CancellationToken>()))
                    .ThrowsAsync(SchemataResourceErrors.Aborted<SchemataUser>());
        var roleMutation = new Mock<IResourceMutation<SchemataRole>>();
        roleMutation.Setup(m => m.DeleteAsync(It.IsAny<SchemataRole>(), null, Operations.Delete, It.IsAny<CancellationToken>()))
                    .ThrowsAsync(SchemataResourceErrors.Aborted<SchemataRole>());

        IdentityResult result;
        if (role) {
            result = await CreateRoleStore(roleMutation).DeleteAsync(new SchemataRole { Name = "r" });
        } else {
            result = await CreateUserStore(userMutation).DeleteAsync(new SchemataUser { Name = "u" });
        }

        Assert.False(result.Succeeded);
        var error = Assert.Single(result.Errors);
        Assert.Equal(nameof(IdentityErrorDescriber.ConcurrencyFailure), error.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_Propagates_Non_Concurrency_Failure(bool role) {
        var failure = new InvalidOperationException("boom");
        var userMutation = new Mock<IResourceMutation<SchemataUser>>();
        userMutation.Setup(m => m.CreateAsync(It.IsAny<SchemataUser>(), null, It.IsAny<CancellationToken>()))
                    .ThrowsAsync(failure);
        var roleMutation = new Mock<IResourceMutation<SchemataRole>>();
        roleMutation.Setup(m => m.CreateAsync(It.IsAny<SchemataRole>(), null, It.IsAny<CancellationToken>()))
                    .ThrowsAsync(failure);

        InvalidOperationException thrown;
        if (role) {
            thrown = await Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateRoleStore(roleMutation).CreateAsync(new SchemataRole { Name = "r" }));
        } else {
            thrown = await Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateUserStore(userMutation).CreateAsync(new SchemataUser { Name = "u" }));
        }

        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task Redeem_Code_Updates_The_Recovery_Row_And_Removes_Only_The_Redeemed_Code() {
        var (store, tokens, entry) = CreateRedeemingStore("alpha;bravo");

        var redeemed = await store.RedeemCodeAsync(new SchemataUser { Name = "u" }, "alpha", CancellationToken.None);

        Assert.True(redeemed);
        Assert.Equal("bravo", entry.Value);
        tokens.Verify(r => r.UpdateAsync(entry, It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()), Times.Once);
    }

    [Fact]
    public async Task Redeem_Code_Returns_False_When_The_Concurrent_Update_Loses_The_Stamp() {
        var (store, _, _) = CreateRedeemingStore("alpha;bravo", lostStamp: true);

        var redeemed = await store.RedeemCodeAsync(new SchemataUser { Name = "u" }, "alpha", CancellationToken.None);

        Assert.False(redeemed);
    }

    [Fact]
    public async Task Redeem_Code_Returns_False_When_The_Recovery_Update_Writes_Nothing() {
        var (store, tokens, entry) = CreateRedeemingStore("alpha;bravo");
        tokens.Setup(r => r.UpdateAsync(entry, It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
              .ReturnsAsync(MutationResult.NoWrite);
        var transaction = new Mock<IUnitOfWork>();
        tokens.Setup(r => r.Begin()).Returns(transaction.Object);

        var redeemed = await store.RedeemCodeAsync(new SchemataUser { Name = "u" }, "alpha", CancellationToken.None);

        Assert.False(redeemed);
        transaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static (SeededUserStore Store, Mock<IRepository<SchemataUserToken>> Tokens, SchemataUserToken Entry)
        CreateRedeemingStore(string codes, bool lostStamp = false) {
        var entry = new SchemataUserToken { LoginProvider = "store", Name = "codes", Value = codes };
        var tokens = new Mock<IRepository<SchemataUserToken>>();
        var unit = new Mock<IUnitOfWork>();
        tokens.Setup(r => r.Begin()).Returns(unit.Object);
        if (lostStamp) {
            tokens.Setup(r => r.UpdateAsync(entry, It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
                  .ThrowsAsync(new AbortedException());
        } else {
            tokens.Setup(r => r.UpdateAsync(entry, It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
                  .ReturnsAsync(MutationResult.Applied);
        }

        return (new SeededUserStore(tokens, entry, Mock.Of<IResourceMutation<SchemataUser>>()), tokens, entry);
    }

    private sealed class SeededUserStore(
        Mock<IRepository<SchemataUserToken>> tokens,
        SchemataUserToken                    entry,
        IResourceMutation<SchemataUser>      mutation
    ) : SchemataUserStore<SchemataUser, SchemataRole, SchemataUserClaim, SchemataUserRole, SchemataUserLogin, SchemataUserToken, SchemataRoleClaim>(
        Mock.Of<IRepository<SchemataUserClaim>>(),
        Mock.Of<IRepository<SchemataUserRole>>(),
        Mock.Of<IRepository<SchemataUserLogin>>(),
        tokens.Object,
        mutation,
        Mock.Of<IServiceProvider>()
    )
    {
        protected override Task<SchemataUserToken?> FindTokenAsync(
            SchemataUser      user,
            string            loginProvider,
            string            name,
            CancellationToken ct
        ) {
            return Task.FromResult<SchemataUserToken?>(entry);
        }
    }

    private static SchemataUserStore<SchemataUser, SchemataRole, SchemataUserClaim, SchemataUserRole, SchemataUserLogin, SchemataUserToken, SchemataRoleClaim> CreateUserStore(Mock<IResourceMutation<SchemataUser>> mutation) {
        return new(
            Mock.Of<IRepository<SchemataUserClaim>>(),
            Mock.Of<IRepository<SchemataUserRole>>(),
            Mock.Of<IRepository<SchemataUserLogin>>(),
            Mock.Of<IRepository<SchemataUserToken>>(),
            mutation.Object,
            Mock.Of<IServiceProvider>()
        );
    }

    private static SchemataRoleStore<SchemataRole, SchemataRoleClaim, SchemataUserRole> CreateRoleStore(Mock<IResourceMutation<SchemataRole>> mutation) {
        return new(
            Mock.Of<IRepository<SchemataRoleClaim>>(),
            Mock.Of<IRepository<SchemataUserRole>>(),
            mutation.Object,
            Mock.Of<IServiceProvider>()
        );
    }
}
