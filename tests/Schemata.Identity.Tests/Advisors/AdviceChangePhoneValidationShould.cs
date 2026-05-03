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

public class AdviceChangePhoneValidationShould
{
    private static readonly ClaimsPrincipal Anonymous = new();

    private static readonly ClaimsPrincipal Alice
        = new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, "42")], "test"));

    private static SchemataUserManager<SchemataUser> MockUserManager(string? phone = "+15551234567") {
        var store = new Mock<ICompositeUserStore>();
        store.Setup(s => s.FindByIdAsync("42", It.IsAny<CancellationToken>()))
             .ReturnsAsync(new SchemataUser { Id = 42, UserName = "alice", PhoneNumber = phone });
        var sp = new ServiceCollection().BuildServiceProvider();
        return new(sp, store.Object, Options.Create(new IdentityOptions()), new PasswordHasher<SchemataUser>(), [], [],
                   new UpperInvariantLookupNormalizer(), new(), NullLogger<SchemataUserManager<SchemataUser>>.Instance);
    }

    [Fact]
    public async Task ThrowsNotFound_WhenUserNotResolved() {
        var advisor = new AdviceChangePhoneValidation<SchemataUser>(MockUserManager());
        var ctx     = new AdviceContext(null!);
        var request = new ProfileRequest { PhoneNumber = "+15559999999" };

        await Assert.ThrowsAsync<NotFoundException>(() => advisor.AdviseAsync(
                                                        ctx, request, IdentityOperation.ChangePhone, Anonymous));
    }

    [Fact]
    public async Task ThrowsValidation_WhenPhoneEmpty() {
        var advisor = new AdviceChangePhoneValidation<SchemataUser>(MockUserManager());
        var ctx     = new AdviceContext(null!);
        var request = new ProfileRequest { PhoneNumber = "" };

        await Assert.ThrowsAsync<ValidationException>(() => advisor.AdviseAsync(
                                                          ctx, request, IdentityOperation.ChangePhone, Alice));
    }

    [Fact]
    public async Task ThrowsNoContent_WhenSamePhone() {
        var advisor = new AdviceChangePhoneValidation<SchemataUser>(MockUserManager());
        var ctx     = new AdviceContext(null!);
        var request = new ProfileRequest { PhoneNumber = "+15551234567" };

        await Assert.ThrowsAsync<NoContentException>(() => advisor.AdviseAsync(
                                                         ctx, request, IdentityOperation.ChangePhone, Alice));
    }

    [Fact]
    public async Task Continues_WhenNewPhoneDifferent() {
        var advisor = new AdviceChangePhoneValidation<SchemataUser>(MockUserManager());
        var ctx     = new AdviceContext(null!);
        var request = new ProfileRequest { PhoneNumber = "+15559999999" };

        var result = await advisor.AdviseAsync(ctx, request, IdentityOperation.ChangePhone, Alice);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task SkipsForOtherOperations() {
        var advisor = new AdviceChangePhoneValidation<SchemataUser>(MockUserManager());
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
