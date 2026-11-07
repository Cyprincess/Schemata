using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Identity.Skeleton.Services;
using Schemata.Identity.Skeleton.Stores;
using Xunit;

namespace Schemata.Identity.Tests;

[Trait("Layer", "Unit")]
public sealed class UsernameNormalizationShould
{
    [Theory]
    [InlineData("Ｆｕｓｓｂａｌｌ", "fussball")]
    [InlineData("Σ", "σ")]
    [InlineData("ς", "ς")]
    [InlineData("fußball", "fußball")]
    [InlineData("e\u0301", "é")]
    [InlineData("Firstname Middlename Lastname", "firstname middlename lastname")]
    [InlineData("l·l", "l·l")]
    [InlineData("\u0375α", "\u0375α")]
    [InlineData("א\u05F3", "א\u05F3")]
    [InlineData("カ・タ", "カ・タ")]
    [InlineData("\u0915\u094D\u200C", "\u0915\u094D\u200C")]
    [InlineData("אב", "אב")]
    public void NormalizeName_Applies_Rfc8265_UsernameCaseMapped(string input, string expected) {
        var normalizer = new UsernameCaseMappedNormalizer();

        Assert.Equal(expected, normalizer.NormalizeName(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData("two  spaces")]
    [InlineData("henryⅣ")]
    [InlineData("∞")]
    [InlineData("\u0378")]
    [InlineData("a·b")]
    [InlineData("\u200C")]
    [InlineData("aא")]
    [InlineData("אa")]
    [InlineData("\u0661\u06F1")]
    public void NormalizeName_Rejects_Invalid_Usernames(string input) {
        var normalizer = new UsernameCaseMappedNormalizer();

        Assert.Throws<FormatException>(() => normalizer.NormalizeName(input));
    }

    [Fact]
    public void NormalizeName_Does_Not_Apply_Naive_Case_Folding() {
        var normalizer = new UsernameCaseMappedNormalizer();

        Assert.NotEqual(normalizer.NormalizeName("fussball"), normalizer.NormalizeName("fußball"));
        Assert.NotEqual(normalizer.NormalizeName("Σ"), normalizer.NormalizeName("ς"));
    }
}

[Trait("Layer", "Unit")]
public sealed class UserProvisioningShould
{
    [Fact]
    public async Task Outer_Transaction_Is_Not_Committed_By_The_Manager() {
        var (manager, _, provisioning) = CreateManager();
        var transaction = new Mock<IUnitOfWork>();
        var user = new SchemataUser { UserName = "outer" };
        provisioning.Setup(value => value.CreateAsync(user, transaction.Object, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(IdentityResult.Success);

        var result = await manager.CreateAsync(user, transaction.Object);

        Assert.True(result.Succeeded);
        provisioning.Verify(value => value.CreateAsync(user, transaction.Object, It.IsAny<CancellationToken>()), Times.Once);
        provisioning.Verify(value => value.BeginProvisioning(), Times.Never);
        transaction.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Independent_Provisioning_Commits_Exactly_Once() {
        var (manager, _, provisioning) = CreateManager();
        var transaction = new Mock<IUnitOfWork>();
        provisioning.Setup(value => value.BeginProvisioning()).Returns(transaction.Object);
        var user = new SchemataUser { UserName = "independent" };
        provisioning.Setup(value => value.CreateAsync(user, transaction.Object, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(IdentityResult.Success);

        var result = await manager.CreateAsync(user, (IUnitOfWork?)null);

        Assert.True(result.Succeeded);
        provisioning.Verify(value => value.BeginProvisioning(), Times.Once);
        transaction.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        transaction.Verify(value => value.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task Administrative_Activation_Uses_The_Outer_Transaction_And_Rotates_Once() {
        var (manager, _, provisioning) = CreateManager();
        var transaction = new Mock<IUnitOfWork>();
        var user = new SchemataUser { UserName = "active", AdministrativelyActive = false, SecurityStamp = "before" };
        provisioning.Setup(value => value.UpdateAsync(user, transaction.Object, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(IdentityResult.Success);

        var result = await manager.SetAdministrativeActiveAsync(user, true, transaction.Object);

        Assert.True(result.Succeeded);
        Assert.True(user.AdministrativelyActive);
        Assert.NotEqual("before", user.SecurityStamp);
        provisioning.Verify(value => value.UpdateAsync(user, transaction.Object, It.IsAny<CancellationToken>()), Times.Once);
        transaction.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Missing_Capability_Fails_Before_Validation_Or_Staging() {
        var store = new Mock<IUserStore<SchemataUser>>(MockBehavior.Strict);
        store.Setup(value => value.Dispose());
        using var manager = CreateManager(store.Object);
        var transaction = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var user = new SchemataUser { UserName = "unsupported" };

        await Assert.ThrowsAsync<NotSupportedException>(() => manager.CreateAsync(user, transaction.Object));

        transaction.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_Usernames_Are_Rejected_Before_A_User_Is_Staged(string? userName) {
        var (manager, _, provisioning) = CreateManager();
        var transaction = new Mock<IUnitOfWork>();
        var user = new SchemataUser { UserName = userName };

        var result = await manager.CreateAsync(user, transaction.Object);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == nameof(IdentityErrorDescriber.InvalidUserName));
        provisioning.Verify(value => value.CreateAsync(It.IsAny<SchemataUser>(), It.IsAny<IUnitOfWork>(), It.IsAny<CancellationToken>()), Times.Never);
        transaction.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static (SchemataUserManager<SchemataUser> Manager, Mock<IUserStore<SchemataUser>> Store,
        Mock<IUserProvisioningStore<SchemataUser>> Provisioning) CreateManager() {
        var store = new Mock<IUserStore<SchemataUser>>();
        var provisioning = store.As<IUserProvisioningStore<SchemataUser>>();
        store.Setup(value => value.GetUserNameAsync(It.IsAny<SchemataUser>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((SchemataUser user, CancellationToken _) => user.UserName);
        store.Setup(value => value.GetNormalizedUserNameAsync(It.IsAny<SchemataUser>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((SchemataUser user, CancellationToken _) => user.NormalizedUserName);
        store.Setup(value => value.FindByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((SchemataUser?)null);
        store.Setup(value => value.SetNormalizedUserNameAsync(It.IsAny<SchemataUser>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
             .Callback((SchemataUser user, string? name, CancellationToken _) => user.NormalizedUserName = name)
             .Returns(Task.CompletedTask);
        store.As<IUserSecurityStampStore<SchemataUser>>()
             .Setup(value => value.SetSecurityStampAsync(It.IsAny<SchemataUser>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .Callback((SchemataUser user, string stamp, CancellationToken _) => user.SecurityStamp = stamp)
             .Returns(Task.CompletedTask);
        return (CreateManager(store.Object), store, provisioning);
    }

    private static SchemataUserManager<SchemataUser> CreateManager(IUserStore<SchemataUser> store) {
        return new(Mock.Of<IServiceProvider>(), store, Options.Create(new IdentityOptions()),
            new PasswordHasher<SchemataUser>(), [new PrecisUserValidator<SchemataUser>()], [],
            new UsernameCaseMappedNormalizer(), new IdentityErrorDescriber(),
            NullLogger<SchemataUserManager<SchemataUser>>.Instance);
    }
}
