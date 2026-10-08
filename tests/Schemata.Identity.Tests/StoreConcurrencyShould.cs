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

    private static SchemataUserStore<SchemataUser> CreateUserStore(Mock<IResourceMutation<SchemataUser>> mutation) {
        return new(
            Mock.Of<IRepository<SchemataUserClaim>>(),
            Mock.Of<IRepository<SchemataUserRole>>(),
            Mock.Of<IRepository<SchemataUserLogin>>(),
            Mock.Of<IRepository<SchemataUserToken>>(),
            mutation.Object,
            Mock.Of<IServiceProvider>()
        );
    }

    private static SchemataRoleStore<SchemataRole> CreateRoleStore(Mock<IResourceMutation<SchemataRole>> mutation) {
        return new(
            Mock.Of<IRepository<SchemataRoleClaim>>(),
            Mock.Of<IRepository<SchemataUserRole>>(),
            mutation.Object,
            Mock.Of<IServiceProvider>()
        );
    }
}
