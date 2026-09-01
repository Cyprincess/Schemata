using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Models;
using Xunit;
using AspNetIdentityResult = Microsoft.AspNetCore.Identity.IdentityResult;

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
    public async Task ChangePassword_Uses_Old_And_New_Password() {
        using var host = new IdentityHandlerTestHost();
        host.Users.Setup(value => value.GetUserAsync(Principal)).ReturnsAsync(User);
        host.Users.Setup(value => value.ChangePasswordAsync(User, "old-secret", "new-secret"))
                  .ReturnsAsync(AspNetIdentityResult.Success);

        var result = await host.Handler.ChangePasswordAsync(new ProfileRequest {
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
            new AuthenticatorRequest(), Principal, CancellationToken.None);

        Assert.Equal(IdentityStatus.Success, result.Status);
        host.Users.Verify(value => value.SetTwoFactorEnabledAsync(User, true), Times.Once);
    }
}
