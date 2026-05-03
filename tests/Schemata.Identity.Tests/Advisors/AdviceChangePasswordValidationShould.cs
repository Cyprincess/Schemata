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

public class AdviceChangePasswordValidationShould
{
    private static readonly ClaimsPrincipal Anonymous = new();

    private static readonly ClaimsPrincipal Alice
        = new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, "42")], "test"));

    private static SchemataUserManager<SchemataUser> MockUserManager() {
        var store = new Mock<ICompositeUserStore>();
        store.Setup(s => s.FindByIdAsync("42", It.IsAny<CancellationToken>()))
             .ReturnsAsync(new SchemataUser { Id = 42, UserName = "alice" });
        var sp = new ServiceCollection().BuildServiceProvider();
        return new(sp, store.Object, Options.Create(new IdentityOptions()), new PasswordHasher<SchemataUser>(), [], [],
                   new UpperInvariantLookupNormalizer(), new(), NullLogger<SchemataUserManager<SchemataUser>>.Instance);
    }

    [Fact]
    public async Task ThrowsNotFound_WhenUserNotResolved() {
        var advisor = new AdviceChangePasswordValidation<SchemataUser>(MockUserManager());
        var ctx     = new AdviceContext(null!);
        var request = new ProfileRequest { OldPassword = "old", NewPassword = "new" };

        await Assert.ThrowsAsync<NotFoundException>(() => advisor.AdviseAsync(
                                                        ctx, request, IdentityOperation.ChangePassword, Anonymous));
    }

    [Fact]
    public async Task ThrowsValidation_WhenOldPasswordEmpty() {
        var advisor = new AdviceChangePasswordValidation<SchemataUser>(MockUserManager());
        var ctx     = new AdviceContext(null!);
        var request = new ProfileRequest { NewPassword = "new" };

        await Assert.ThrowsAsync<ValidationException>(() => advisor.AdviseAsync(
                                                          ctx, request, IdentityOperation.ChangePassword, Alice));
    }

    [Fact]
    public async Task ThrowsValidation_WhenNewPasswordEmpty() {
        var advisor = new AdviceChangePasswordValidation<SchemataUser>(MockUserManager());
        var ctx     = new AdviceContext(null!);
        var request = new ProfileRequest { OldPassword = "old" };

        await Assert.ThrowsAsync<ValidationException>(() => advisor.AdviseAsync(
                                                          ctx, request, IdentityOperation.ChangePassword, Alice));
    }

    [Fact]
    public async Task ThrowsNoContent_WhenSamePassword() {
        var advisor = new AdviceChangePasswordValidation<SchemataUser>(MockUserManager());
        var ctx     = new AdviceContext(null!);
        var request = new ProfileRequest { OldPassword = "same", NewPassword = "same" };

        await Assert.ThrowsAsync<NoContentException>(() => advisor.AdviseAsync(
                                                         ctx, request, IdentityOperation.ChangePassword, Alice));
    }

    [Fact]
    public async Task CaseSensitiveComparison() {
        var advisor = new AdviceChangePasswordValidation<SchemataUser>(MockUserManager());
        var ctx     = new AdviceContext(null!);
        var request = new ProfileRequest { OldPassword = "Password", NewPassword = "password" };

        var result = await advisor.AdviseAsync(ctx, request, IdentityOperation.ChangePassword, Alice);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task SkipsForOtherOperations() {
        var advisor = new AdviceChangePasswordValidation<SchemataUser>(MockUserManager());
        var ctx     = new AdviceContext(null!);
        var request = new ProfileRequest();

        var result = await advisor.AdviseAsync(ctx, request, IdentityOperation.Login, Anonymous);

        Assert.Equal(AdviseResult.Continue, result);
    }

    #region Nested type: ICompositeUserStore

    public interface ICompositeUserStore : IUserStore<SchemataUser>, IUserEmailStore<SchemataUser>,
                                           IUserPhoneNumberStore<SchemataUser>, IUserRoleStore<SchemataUser>,
                                           IUserDisplayNameStore<SchemataUser>, IUserPrincipalNameStore<SchemataUser>;

    #endregion
}
