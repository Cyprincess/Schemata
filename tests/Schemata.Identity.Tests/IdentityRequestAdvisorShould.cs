using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Advice;
using Schemata.Identity.Foundation.Handlers;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Advisors;
using Schemata.Identity.Skeleton.Claims;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Models;
using Xunit;

namespace Schemata.Identity.Tests;

public class IdentityRequestAdvisorShould
{
    private static readonly ClaimsPrincipal Principal = new(new ClaimsIdentity("test"));

    public static IEnumerable<object[]> OperationStates() {
        foreach (var operation in System.Enum.GetValues<IdentityOperation>()) {
            yield return [operation, IdentityStatus.Success];
            yield return [operation, IdentityStatus.Challenge];
        }
    }

    [Theory]
    [MemberData(nameof(OperationStates))]
    public Task Handle_Each_Operation_As_Success_Or_Challenge(
        IdentityOperation operation,
        IdentityStatus    status
    ) {
        return operation switch {
            IdentityOperation.Register => AssertHandled<RegisterRequest, ClaimsPrincipal>(
                operation, status, handler => handler.RegisterAsync(new(), Principal)),
            IdentityOperation.Login => AssertHandled<LoginRequest, ClaimsPrincipal>(
                operation, status, handler => handler.LoginAsync(new(), Principal)),
            IdentityOperation.Refresh => AssertHandled<Unit, ClaimsPrincipal>(
                operation, status, handler => handler.RefreshAsync(null, Principal)),
            IdentityOperation.Profile => AssertHandled<Unit, ClaimsStore>(
                operation, status, handler => handler.ProfileAsync(Principal)),
            IdentityOperation.ChangeEmail => AssertHandled<ProfileRequest, Unit>(
                operation, status, handler => handler.ChangeEmailAsync(new(), Principal)),
            IdentityOperation.ChangePhone => AssertHandled<ProfileRequest, Unit>(
                operation, status, handler => handler.ChangePhoneAsync(new(), Principal)),
            IdentityOperation.ChangePassword => AssertHandled<ProfileRequest, Unit>(
                operation, status, handler => handler.ChangePasswordAsync(new(), Principal)),
            IdentityOperation.Forgot => AssertHandled<ForgetRequest, Unit>(
                operation, status, handler => handler.ForgotAsync(new(), Principal)),
            IdentityOperation.Reset => AssertHandled<ResetRequest, Unit>(
                operation, status, handler => handler.ResetAsync(new(), Principal)),
            IdentityOperation.Confirm => AssertHandled<ConfirmRequest, Unit>(
                operation, status, handler => handler.ConfirmAsync(new(), Principal)),
            IdentityOperation.Code => AssertHandled<ForgetRequest, Unit>(
                operation, status, handler => handler.CodeAsync(new(), Principal)),
            IdentityOperation.Authenticator => AssertHandled<Unit, AuthenticatorResponse>(
                operation, status, handler => handler.AuthenticatorAsync(Principal)),
            IdentityOperation.Enroll => AssertHandled<AuthenticatorRequest, Unit>(
                operation, status, handler => handler.EnrollAsync(new(), Principal)),
            IdentityOperation.Downgrade => AssertHandled<AuthenticatorRequest, Unit>(
                operation, status, handler => handler.DowngradeAsync(new(), Principal)),
            _ => throw new Xunit.Sdk.XunitException($"Unhandled identity operation '{operation}'."),
        };
    }

    private static async Task AssertHandled<TRequest, TResponse>(
        IdentityOperation operation,
        IdentityStatus    status,
        System.Func<IdentityHandler<SchemataUser>, Task<IdentityResult<TResponse>>> invoke
    ) {
        var expected = status == IdentityStatus.Success
            ? IdentityResult<TResponse>.Success(default)
            : IdentityResult<TResponse>.Challenge();
        var advisor = new Mock<IIdentityRequestAdvisor<TRequest>>();
        advisor.SetupGet(value => value.Order).Returns(0);
        advisor.Setup(value => value.AdviseAsync(
                          It.IsAny<AdviceContext>(),
                          It.IsAny<TRequest>(),
                          operation,
                          Principal,
                          It.IsAny<CancellationToken>()))
               .Returns((AdviceContext ctx, TRequest _, IdentityOperation _, ClaimsPrincipal _, CancellationToken _) => {
                   ctx.Set(expected);
                   return Task.FromResult(AdviseResult.Handle);
               });
        using var host = new IdentityHandlerTestHost(services => services.AddSingleton(advisor.Object));
        var userInvocations   = host.Users.Invocations.Count;
        var signInInvocations = host.SignIn.Invocations.Count;

        var actual = await invoke(host.Handler);

        Assert.Same(expected, actual);
        Assert.Equal(status, actual.Status);
        advisor.Verify(value => value.AdviseAsync(
                           It.IsAny<AdviceContext>(),
                           It.IsAny<TRequest>(),
                           operation,
                           Principal,
                           It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(userInvocations, host.Users.Invocations.Count);
        Assert.Equal(signInInvocations, host.SignIn.Invocations.Count);
    }
}
