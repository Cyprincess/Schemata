using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Moq;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Xunit;
using AspNetIdentityResult = Microsoft.AspNetCore.Identity.IdentityResult;
using Schemata.Identity.Tests.Fixtures;

namespace Schemata.Identity.Tests;

public class IdentityCredentialHandlerShould
{
    private static readonly SchemataUser User = new() { UserName = "alice" };

    private static readonly ClaimsPrincipal Principal = new(new ClaimsIdentity("test"));

    [Fact]
    public async Task ChangeEmail_Generates_And_Sends_Confirmation_Code() {
        using var host = new IdentityHandlerTestHost();
        host.Users.Setup(value => value.GetUserAsync(Principal)).ReturnsAsync(User);
        host.Users.Setup(value => value.GenerateChangeEmailTokenAsync(User, "new@example.com"))
                  .ReturnsAsync("email-code");
        host.Mail.Setup(value => value.SendConfirmationCodeAsync(User, "new@example.com", "email-code"))
                 .Returns(Task.CompletedTask);

        var result = await host.Handler.ChangeEmailAsync(
            new() { EmailAddress = "new@example.com" }, Principal, CancellationToken.None);

        Assert.Equal(IdentityStatus.Success, result.Status);
        host.Users.Verify(value => value.GenerateChangeEmailTokenAsync(
                              User, "new@example.com"), Times.Once);
        host.Mail.Verify(value => value.SendConfirmationCodeAsync(
                             User, "new@example.com", "email-code"), Times.Once);
    }

    [Fact]
    public async Task ChangePhone_Generates_And_Sends_Confirmation_Code() {
        using var host = new IdentityHandlerTestHost();
        host.Users.Setup(value => value.GetUserAsync(Principal)).ReturnsAsync(User);
        host.Users.Setup(value => value.GenerateChangePhoneNumberTokenAsync(User, "+12025550123"))
                  .ReturnsAsync("phone-code");
        host.Message.Setup(value => value.SendConfirmationCodeAsync(User, "+12025550123", "phone-code"))
                    .Returns(Task.CompletedTask);

        var result = await host.Handler.ChangePhoneAsync(
            new() { PhoneNumber = "+12025550123" }, Principal, CancellationToken.None);

        Assert.Equal(IdentityStatus.Success, result.Status);
        host.Users.Verify(value => value.GenerateChangePhoneNumberTokenAsync(
                              User, "+12025550123"), Times.Once);
        host.Message.Verify(value => value.SendConfirmationCodeAsync(
                                User, "+12025550123", "phone-code"), Times.Once);
    }

    [Fact]
    public async Task Confirm_From_An_Authenticated_Initiator_Changes_That_Account_With_The_Requested_Contact() {
        using var host = new IdentityHandlerTestHost();
        host.Users.Setup(value => value.GetUserAsync(Principal)).ReturnsAsync(User);
        host.Users.Setup(value => value.ChangeEmailAsync(User, "new@example.com", "email-code"))
                  .ReturnsAsync(AspNetIdentityResult.Success);

        var result = await host.Handler.ConfirmAsync(
            new() { EmailAddress = "new@example.com", Code = "email-code" }, Principal, CancellationToken.None);

        Assert.Equal(IdentityStatus.Success, result.Status);
        // The initiating account receives the change; the requested contact never resolves a
        // different account.
        host.Users.Verify(value => value.ChangeEmailAsync(User, "new@example.com", "email-code"), Times.Once);
        host.Users.Verify(value => value.FindByEmailAsync("new@example.com"), Times.Never);
    }

    [Fact]
    public async Task Confirm_From_An_Anonymous_Caller_Resolves_The_Account_By_The_Current_Contact() {
        using var host = new IdentityHandlerTestHost();
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        host.Users.Setup(value => value.FindByEmailAsync("alice@example.com")).ReturnsAsync(User);
        host.Users.Setup(value => value.ChangeEmailAsync(User, "alice@example.com", "email-code"))
                  .ReturnsAsync(AspNetIdentityResult.Success);

        var result = await host.Handler.ConfirmAsync(
            new() { EmailAddress = "alice@example.com", Code = "email-code" }, anonymous, CancellationToken.None);

        Assert.Equal(IdentityStatus.Success, result.Status);
        host.Users.Verify(value => value.ChangeEmailAsync(User, "alice@example.com", "email-code"), Times.Once);
    }

    [Fact]
    public async Task Confirm_With_A_Code_That_Fails_Verification_Shapes_As_Missing_Account() {
        using var host = new IdentityHandlerTestHost();
        host.Users.Setup(value => value.GetUserAsync(Principal)).ReturnsAsync(User);
        // A token minted for another purpose or contact fails the UserManager's verification.
        host.Users.Setup(value => value.ChangeEmailAsync(User, "new@example.com", "wrong-purpose-code"))
                  .ReturnsAsync(AspNetIdentityResult.Failed(new IdentityError { Code = "x", Description = "y" }));

        var error = await Assert.ThrowsAsync<NoContentException>(() => host.Handler.ConfirmAsync(
            new() { EmailAddress = "new@example.com", Code = "wrong-purpose-code" }, Principal, CancellationToken.None));

        Assert.NotNull(error);
    }

    [Fact]
    public async Task ChangePassword_Uses_Old_And_New_Password() {
        using var host = new IdentityHandlerTestHost();
        host.Users.Setup(value => value.GetUserAsync(Principal)).ReturnsAsync(User);
        host.Users.Setup(value => value.ChangePasswordAsync(User, "old-secret", "new-secret"))
                  .ReturnsAsync(AspNetIdentityResult.Success);

        var result = await host.Handler.ChangePasswordAsync(new() {
            OldPassword = "old-secret",
            NewPassword = "new-secret",
        }, Principal, CancellationToken.None);

        Assert.Equal(IdentityStatus.Success, result.Status);
        host.Users.Verify(value => value.ChangePasswordAsync(User, "old-secret", "new-secret"), Times.Once);
    }

    [Fact]
    public async Task Authenticator_When_Enabled_Returns_Remembered_State_And_Recovery_Count() {
        using var host = new IdentityHandlerTestHost();
        host.Users.Setup(value => value.GetUserAsync(Principal)).ReturnsAsync(User);
        host.Users.Setup(value => value.GetTwoFactorEnabledAsync(User)).ReturnsAsync(true);
        host.SignIn.Setup(value => value.IsTwoFactorClientRememberedAsync(User)).ReturnsAsync(true);
        host.Users.Setup(value => value.CountRecoveryCodesAsync(User)).ReturnsAsync(4);

        var result = await host.Handler.AuthenticatorAsync(Principal, CancellationToken.None);

        Assert.Equal(IdentityStatus.Success, result.Status);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.IsTwoFactorEnabled);
        Assert.True(result.Data.IsMachineRemembered);
        Assert.Equal(4, result.Data.RecoveryCodesLeft);
    }

    [Fact]
    public async Task Enroll_Enables_TwoFactor() {
        using var host = new IdentityHandlerTestHost();
        host.Users.Setup(value => value.GetUserAsync(Principal)).ReturnsAsync(User);
        host.Users.Setup(value => value.SetTwoFactorEnabledAsync(User, true))
                  .ReturnsAsync(AspNetIdentityResult.Success);

        var result = await host.Handler.EnrollAsync(
            new(), Principal, CancellationToken.None);

        Assert.Equal(IdentityStatus.Success, result.Status);
        host.Users.Verify(value => value.SetTwoFactorEnabledAsync(User, true), Times.Once);
    }

    [Fact]
    public async Task Surface_A_Failed_Two_Factor_Mutation_As_A_Validation_Error_Instead_Of_Success() {
        using var host = new IdentityHandlerTestHost();
        host.Users.Setup(value => value.GetUserAsync(Principal)).ReturnsAsync(User);
        host.Users.Setup(value => value.GetTwoFactorEnabledAsync(User)).ReturnsAsync(false);
        host.Users.Setup(value => value.VerifyTwoFactorTokenAsync(User, It.IsAny<string>(), "123456"))
                  .ReturnsAsync(true);
        var failed = AspNetIdentityResult.Failed(new IdentityError {
            Code = "ConcurrencyFailure", Description = "Optimistic concurrency failure",
        });
        host.Users.Setup(value => value.ResetAuthenticatorKeyAsync(User)).ReturnsAsync(failed);
        host.Users.Setup(value => value.SetTwoFactorEnabledAsync(User, false)).ReturnsAsync(failed);

        var authenticator = await Assert.ThrowsAsync<ValidationException>(
            () => host.Handler.AuthenticatorAsync(Principal, CancellationToken.None));
        var downgrade = await Assert.ThrowsAsync<ValidationException>(
            () => host.Handler.DowngradeAsync(new() { TwoFactorCode = "123456" }, Principal, CancellationToken.None));

        Assert.NotNull(authenticator.Details);
        Assert.NotNull(downgrade.Details);
        foreach (var violation in authenticator.Details.OfType<BadRequestDetail>().Single().FieldViolations!
                                  .Concat(downgrade.Details.OfType<BadRequestDetail>().Single().FieldViolations!)) {
            Assert.Equal("CONCURRENCY_FAILURE", violation.Reason);
            Assert.Equal("Optimistic concurrency failure", violation.Description);
        }
    }
}
