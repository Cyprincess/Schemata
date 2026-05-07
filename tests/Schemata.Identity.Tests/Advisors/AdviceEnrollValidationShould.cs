using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Identity.Foundation.Advisors;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Identity.Skeleton.Models;
using Schemata.Identity.Skeleton.Stores;
using Xunit;

namespace Schemata.Identity.Tests.Advisors;

public class AdviceEnrollValidationShould
{
    private static readonly ClaimsPrincipal Anonymous = new();

    private static readonly ClaimsPrincipal Alice = new(
        new ClaimsIdentity([new(ClaimTypes.NameIdentifier, "42")], "test")
    );

    private static SchemataUserManager<SchemataUser> MockUserManager() {
        var store = new Mock<ICompositeUserStore>();
        store.Setup(s => s.FindByIdAsync("42", It.IsAny<CancellationToken>()))
             .ReturnsAsync(new SchemataUser { Uid = Guid.NewGuid(), UserName = "alice" });
        var sp = new ServiceCollection().BuildServiceProvider();
        return new(
            sp,
            store.Object,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<SchemataUser>(),
            [],
            [],
            new UpperInvariantLookupNormalizer(),
            new(),
            NullLogger<SchemataUserManager<SchemataUser>>.Instance
        );
    }

    [Fact]
    public async Task ThrowsNotFound_WhenUserNotResolved() {
        var manager = MockUserManager();
        var advisor = new AdviceEnrollValidation<SchemataUser>(manager);
        var ctx     = new AdviceContext(null!);
        var request = new AuthenticatorRequest { TwoFactorCode = "123456" };

        await Assert.ThrowsAsync<NotFoundException>(() => advisor.AdviseAsync(
                                                        ctx,
                                                        request,
                                                        IdentityOperation.Enroll,
                                                        Anonymous
                                                    )
        );
    }

    [Fact]
    public async Task ThrowsValidation_WhenTwoFactorCodeEmpty() {
        var manager = MockUserManager();
        var advisor = new AdviceEnrollValidation<SchemataUser>(manager);
        var ctx     = new AdviceContext(null!);
        var request = new AuthenticatorRequest();

        await Assert.ThrowsAsync<ValidationException>(() => advisor.AdviseAsync(
                                                          ctx,
                                                          request,
                                                          IdentityOperation.Enroll,
                                                          Alice
                                                      )
        );
    }

    [Fact]
    public async Task SkipsForNonEnrollOperations() {
        var manager = MockUserManager();
        var advisor = new AdviceEnrollValidation<SchemataUser>(manager);
        var ctx     = new AdviceContext(null!);
        var request = new AuthenticatorRequest();

        var result = await advisor.AdviseAsync(ctx, request, IdentityOperation.Downgrade, Anonymous);

        Assert.Equal(AdviseResult.Continue, result);
    }

    #region Nested type: ICompositeUserStore

    public interface ICompositeUserStore : IUserStore<SchemataUser>, IUserEmailStore<SchemataUser>,
                                           IUserPhoneNumberStore<SchemataUser>, IUserRoleStore<SchemataUser>,
                                           IUserDisplayNameStore<SchemataUser>, IUserPrincipalNameStore<SchemataUser>;

    #endregion
}
