using Schemata.Identity.Tests.Fixtures;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Xunit;
using AspNetIdentityResult = Microsoft.AspNetCore.Identity.IdentityResult;

namespace Schemata.Identity.Tests;

public class IdentityRecoveryHandlerShould
{
    private const string Email = "alice@example.com";

    private static readonly SchemataUser User = new() { UserName = "alice", Email = Email };

    private static readonly ClaimsPrincipal Anonymous = new();

    [Fact]
    public async Task Confirm_Changes_Email_With_Confirmation_Code() {
        using var host = new IdentityHandlerTestHost();
        host.Users.Setup(value => value.FindByEmailAsync(Email)).ReturnsAsync(User);
        host.Users.Setup(value => value.ChangeEmailAsync(User, Email, "confirm-code"))
                  .ReturnsAsync(AspNetIdentityResult.Success);

        var result = await host.Handler.ConfirmAsync(new() {
            EmailAddress = Email,
            Code         = "confirm-code",
        }, Anonymous, CancellationToken.None);

        Assert.Equal(IdentityStatus.Success, result.Status);
        host.Users.Verify(value => value.ChangeEmailAsync(User, Email, "confirm-code"), Times.Once);
    }

    [Fact]
    public async Task Code_Generates_And_Sends_Email_Confirmation_Code() {
        using var host = new IdentityHandlerTestHost();
        host.Users.Setup(value => value.FindByEmailAsync(Email)).ReturnsAsync(User);
        host.Users.Setup(value => value.GenerateChangeEmailTokenAsync(User, Email))
                  .ReturnsAsync("confirm-code");
        host.Mail.Setup(value => value.SendConfirmationCodeAsync(User, Email, "confirm-code"))
                 .Returns(Task.CompletedTask);

        var result = await host.Handler.CodeAsync(
            new() { EmailAddress = Email }, Anonymous, CancellationToken.None);

        Assert.Equal(IdentityStatus.Success, result.Status);
        host.Users.Verify(value => value.GenerateChangeEmailTokenAsync(User, Email), Times.Once);
        host.Mail.Verify(value => value.SendConfirmationCodeAsync(User, Email, "confirm-code"), Times.Once);
    }

    [Fact]
    public async Task Forgot_Confirmed_Email_Generates_And_Sends_Reset_Code() {
        using var host = new IdentityHandlerTestHost();
        host.Users.Setup(value => value.FindByEmailAsync(Email)).ReturnsAsync(User);
        host.Users.Setup(value => value.IsEmailConfirmedAsync(User)).ReturnsAsync(true);
        host.Users.Setup(value => value.GeneratePasswordResetTokenAsync(User)).ReturnsAsync("reset-code");
        host.Mail.Setup(value => value.SendPasswordResetCodeAsync(User, Email, "reset-code"))
                 .Returns(Task.CompletedTask);

        var result = await host.Handler.ForgotAsync(
            new() { EmailAddress = Email }, Anonymous, CancellationToken.None);

        Assert.Equal(IdentityStatus.Success, result.Status);
        host.Users.Verify(value => value.GeneratePasswordResetTokenAsync(User), Times.Once);
        host.Mail.Verify(value => value.SendPasswordResetCodeAsync(User, Email, "reset-code"), Times.Once);
    }

    [Fact]
    public async Task Forgot_Unconfirmed_Email_Succeeds_Without_Sending_Code() {
        using var host = new IdentityHandlerTestHost();
        host.Users.Setup(value => value.FindByEmailAsync(Email)).ReturnsAsync(User);
        host.Users.Setup(value => value.IsEmailConfirmedAsync(User)).ReturnsAsync(false);

        var result = await host.Handler.ForgotAsync(
            new() { EmailAddress = Email }, Anonymous, CancellationToken.None);

        Assert.Equal(IdentityStatus.Success, result.Status);
        host.Users.Verify(value => value.GeneratePasswordResetTokenAsync(It.IsAny<SchemataUser>()), Times.Never);
        host.Mail.Verify(value => value.SendPasswordResetCodeAsync(
                             It.IsAny<SchemataUser>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Reset_Confirmed_Email_Changes_Password() {
        using var host = new IdentityHandlerTestHost();
        host.Users.Setup(value => value.FindByEmailAsync(Email)).ReturnsAsync(User);
        host.Users.Setup(value => value.IsEmailConfirmedAsync(User)).ReturnsAsync(true);
        host.Users.Setup(value => value.ResetPasswordAsync(User, "reset-code", "new-secret"))
                  .ReturnsAsync(AspNetIdentityResult.Success);

        var result = await host.Handler.ResetAsync(new() {
            EmailAddress = Email,
            Code         = "reset-code",
            Password     = "new-secret",
        }, Anonymous, CancellationToken.None);

        Assert.Equal(IdentityStatus.Success, result.Status);
        host.Users.Verify(value => value.ResetPasswordAsync(User, "reset-code", "new-secret"), Times.Once);
    }
}
