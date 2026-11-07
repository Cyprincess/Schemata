using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Identity.Skeleton.Advisors;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Identity.Skeleton.Services;
using Schemata.Identity.Skeleton.Stores;
using Xunit;

namespace Schemata.Identity.Tests;

[Trait("Layer", "Unit")]
public sealed class UserManagerNormalizationShould
{
    [Fact]
    public async Task Create_Normalizes_Final_Fields_Before_Transactional_Uniqueness() {
        var store = new Mock<IUserStore<SchemataUser>>();
        var provisioning = store.As<IUserProvisioningStore<SchemataUser>>();
        var transaction = new Mock<IUnitOfWork>();
        provisioning.Setup(value => value.CreateAsync(It.IsAny<SchemataUser>(), transaction.Object, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(IdentityResult.Success);
        var advisor = new Mock<IIdentityAliasUniquenessAdvisor<SchemataUser>>();
        advisor.SetupGet(value => value.Order).Returns(10);
        advisor.Setup(value => value.AdviseAsync(It.IsAny<AdviceContext>(), Operations.Create, It.IsAny<SchemataUser>(),
                   transaction.Object, It.IsAny<IList<ErrorFieldViolation>>(), It.IsAny<CancellationToken>()))
               .Callback((AdviceContext _, Operations _, SchemataUser user, IUnitOfWork _, IList<ErrorFieldViolation> _,
                   CancellationToken _) => {
                   Assert.Equal("name:FinalUser", user.NormalizedUserName);
                   Assert.Equal("email:final@example.com", user.NormalizedEmail);
                   Assert.Equal("phone:+15550001111", user.PhoneNumber);
               })
               .ReturnsAsync(AdviseResult.Continue);
        using var manager = Manager(store.Object, [advisor.Object], out var name, out var email, out var phone);
        var user = new SchemataUser { UserName = "FinalUser", Email = "final@example.com", PhoneNumber = "+15550001111" };

        var result = await manager.CreateAsync(user, transaction.Object);

        Assert.True(result.Succeeded);
        name.Verify(value => value.NormalizeName("FinalUser"), Times.Once);
        email.Verify(value => value.NormalizeEmail("final@example.com"), Times.Once);
        phone.Verify(value => value.NormalizePhoneNumber("+15550001111"), Times.Once);
        provisioning.Verify(value => value.CreateAsync(user, transaction.Object, It.IsAny<CancellationToken>()), Times.Once);
        transaction.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Standard_Update_Owns_One_Serializable_Transaction() {
        var store = new Mock<IUserStore<SchemataUser>>();
        var provisioning = store.As<IUserProvisioningStore<SchemataUser>>();
        var transaction = new Mock<IUnitOfWork>();
        provisioning.Setup(value => value.BeginProvisioning()).Returns(transaction.Object);
        provisioning.Setup(value => value.UpdateAsync(It.IsAny<SchemataUser>(), transaction.Object, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(IdentityResult.Success);
        using var manager = Manager(store.Object, [], out _, out _, out _);
        var user = new SchemataUser { UserName = "FinalUser", Email = "final@example.com", PhoneNumber = "+15550001111" };

        var result = await manager.UpdateAsync(user);

        Assert.True(result.Succeeded);
        provisioning.Verify(value => value.BeginProvisioning(), Times.Once);
        provisioning.Verify(value => value.UpdateAsync(user, transaction.Object, It.IsAny<CancellationToken>()), Times.Once);
        transaction.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        transaction.Verify(value => value.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task Raw_Lookups_Use_Their_Own_Normalizers() {
        var store = new Mock<IUserStore<SchemataUser>>();
        store.Setup(value => value.FindByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((SchemataUser?)null);
        store.As<IUserEmailStore<SchemataUser>>()
             .Setup(value => value.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((SchemataUser?)null);
        store.As<IUserPhoneStore<SchemataUser>>()
             .Setup(value => value.FindByPhoneAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((SchemataUser?)null);
        using var manager = Manager(store.Object, [], out var name, out var email, out var phone);

        Assert.Null(await manager.FindByNameAsync("RawUser"));
        Assert.Null(await manager.FindByEmailAsync("raw@example.com"));
        Assert.Null(await manager.FindByPhoneAsync("+15550001111"));

        store.Verify(value => value.FindByNameAsync("name:RawUser", It.IsAny<CancellationToken>()), Times.Once);
        store.As<IUserEmailStore<SchemataUser>>()
             .Verify(value => value.FindByEmailAsync("email:raw@example.com", It.IsAny<CancellationToken>()), Times.Once);
        store.As<IUserPhoneStore<SchemataUser>>()
             .Verify(value => value.FindByPhoneAsync("phone:+15550001111", It.IsAny<CancellationToken>()), Times.Once);
        name.Verify(value => value.NormalizeEmail(It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task Uniqueness_Advisors_Run_In_Order_And_Block_Before_Staging() {
        var store = new Mock<IUserStore<SchemataUser>>();
        var provisioning = store.As<IUserProvisioningStore<SchemataUser>>();
        var transaction = new Mock<IUnitOfWork>();
        var sequence = new List<int>();
        var first = Advisor(1, AdviseResult.Continue, sequence);
        var second = Advisor(2, AdviseResult.Block, sequence);
        var last = Advisor(3, AdviseResult.Continue, sequence);
        using var manager = Manager(store.Object, [last.Object, second.Object, first.Object], out _, out _, out _);
        var user = new SchemataUser { UserName = "FinalUser" };

        await Assert.ThrowsAsync<ValidationException>(() => manager.CreateAsync(user, transaction.Object));

        Assert.Equal([1, 2], sequence);
        provisioning.Verify(value => value.CreateAsync(It.IsAny<SchemataUser>(), It.IsAny<IUnitOfWork>(), It.IsAny<CancellationToken>()), Times.Never);
        last.Verify(value => value.AdviseAsync(It.IsAny<AdviceContext>(), It.IsAny<Operations>(), It.IsAny<SchemataUser>(),
            It.IsAny<IUnitOfWork>(), It.IsAny<IList<ErrorFieldViolation>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Mock<IIdentityAliasUniquenessAdvisor<SchemataUser>> Advisor(
        int order,
        AdviseResult result,
        ICollection<int> sequence
    ) {
        var advisor = new Mock<IIdentityAliasUniquenessAdvisor<SchemataUser>>();
        advisor.SetupGet(value => value.Order).Returns(order);
        advisor.Setup(value => value.AdviseAsync(It.IsAny<AdviceContext>(), It.IsAny<Operations>(), It.IsAny<SchemataUser>(),
                   It.IsAny<IUnitOfWork>(), It.IsAny<IList<ErrorFieldViolation>>(), It.IsAny<CancellationToken>()))
               .Callback((AdviceContext _, Operations _, SchemataUser _, IUnitOfWork _, IList<ErrorFieldViolation> errors,
                   CancellationToken _) => {
                   sequence.Add(order);
                   if (result == AdviseResult.Block) errors.Add(new ErrorFieldViolation { Reason = "DUPLICATE_ALIAS" });
               })
               .ReturnsAsync(result);
        return advisor;
    }

    private static SchemataUserManager<SchemataUser> Manager(
        IUserStore<SchemataUser> store,
        IEnumerable<IIdentityAliasUniquenessAdvisor<SchemataUser>> advisors,
        out Mock<ILookupNormalizer> name,
        out Mock<IEmailNormalizer> email,
        out Mock<IPhoneNumberNormalizer> phone
    ) {
        name = new Mock<ILookupNormalizer>();
        name.Setup(value => value.NormalizeName(It.IsAny<string?>())).Returns((string? value) => value is null ? null : $"name:{value}");
        email = new Mock<IEmailNormalizer>();
        email.Setup(value => value.NormalizeEmail(It.IsAny<string>())).Returns((string value) => $"email:{value}");
        phone = new Mock<IPhoneNumberNormalizer>();
        phone.Setup(value => value.NormalizePhoneNumber(It.IsAny<string>())).Returns((string value) => $"phone:{value}");
        var services = new ServiceCollection().AddSingleton(name.Object).AddSingleton(email.Object).AddSingleton(phone.Object);
        foreach (var advisor in advisors) services.AddSingleton(advisor);
        var provider = services.BuildServiceProvider();
        return new SchemataUserManager<SchemataUser>(provider, store, Options.Create(new IdentityOptions()),
            new PasswordHasher<SchemataUser>(), [], [], name.Object, new IdentityErrorDescriber(),
            NullLogger<SchemataUserManager<SchemataUser>>.Instance);
    }
}
