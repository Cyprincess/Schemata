using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Identity.Foundation;
using Schemata.Identity.Foundation.Advisors;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Models;
using Xunit;

namespace Schemata.Identity.Tests.Advisors;

public class AdviceIdentityFeatureGateShould
{
    private static readonly ClaimsPrincipal Anonymous = new();

    private static AdviceIdentityFeatureGate<T> Gate<T>(SchemataIdentityOptions opts) {
        var monitor = new Mock<IOptionsMonitor<SchemataIdentityOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(opts);
        return new(monitor.Object);
    }

    [Theory]
    [InlineData(IdentityOperation.Register)]
    [InlineData(IdentityOperation.Login)]
    [InlineData(IdentityOperation.Refresh)]
    [InlineData(IdentityOperation.Profile)]
    [InlineData(IdentityOperation.ChangeEmail)]
    [InlineData(IdentityOperation.ChangePhone)]
    [InlineData(IdentityOperation.ChangePassword)]
    [InlineData(IdentityOperation.Forgot)]
    [InlineData(IdentityOperation.Reset)]
    [InlineData(IdentityOperation.Confirm)]
    [InlineData(IdentityOperation.Code)]
    [InlineData(IdentityOperation.Authenticator)]
    [InlineData(IdentityOperation.Enroll)]
    [InlineData(IdentityOperation.Downgrade)]
    public async Task AllowsOperation_WhenAllFeaturesEnabled(IdentityOperation op) {
        var gate = Gate<object>(new());
        var ctx  = new AdviceContext(null!);

        var result = await gate.AdviseAsync(ctx, null!, op, Anonymous);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task BlocksRegistration_WhenDisabled() {
        var gate = Gate<RegisterRequest>(new() { AllowRegistration = false });
        var ctx  = new AdviceContext(null!);

        await Assert.ThrowsAsync<NotFoundException>(() => gate.AdviseAsync(ctx, new(), IdentityOperation.Register, Anonymous));
    }

    [Theory]
    [InlineData(IdentityOperation.Forgot)]
    [InlineData(IdentityOperation.Reset)]
    public async Task BlocksPasswordReset_WhenDisabled(IdentityOperation op) {
        var gate = Gate<object>(new() { AllowPasswordReset = false });
        var ctx  = new AdviceContext(null!);

        await Assert.ThrowsAsync<NotFoundException>(() => gate.AdviseAsync(ctx, null!, op, Anonymous));
    }

    [Fact]
    public async Task BlocksEmailChange_WhenDisabled() {
        var gate = Gate<ProfileRequest>(new() { AllowEmailChange = false });
        var ctx  = new AdviceContext(null!);

        await Assert.ThrowsAsync<NotFoundException>(() => gate.AdviseAsync(ctx, new(), IdentityOperation.ChangeEmail, Anonymous));
    }

    [Fact]
    public async Task BlocksPhoneChange_WhenDisabled() {
        var gate = Gate<ProfileRequest>(new() { AllowPhoneNumberChange = false });
        var ctx  = new AdviceContext(null!);

        await Assert.ThrowsAsync<NotFoundException>(() => gate.AdviseAsync(ctx, new(), IdentityOperation.ChangePhone, Anonymous));
    }

    [Fact]
    public async Task BlocksPasswordChange_WhenDisabled() {
        var gate = Gate<ProfileRequest>(new() { AllowPasswordChange = false });
        var ctx  = new AdviceContext(null!);

        await Assert.ThrowsAsync<NotFoundException>(() => gate.AdviseAsync(ctx, new(), IdentityOperation.ChangePassword, Anonymous));
    }

    [Theory]
    [InlineData(IdentityOperation.Confirm)]
    [InlineData(IdentityOperation.Code)]
    public async Task BlocksAccountConfirmation_WhenDisabled(IdentityOperation op) {
        var gate = Gate<object>(new() { AllowAccountConfirmation = false });
        var ctx  = new AdviceContext(null!);

        await Assert.ThrowsAsync<NotFoundException>(() => gate.AdviseAsync(ctx, null!, op, Anonymous));
    }

    [Theory]
    [InlineData(IdentityOperation.Authenticator)]
    [InlineData(IdentityOperation.Enroll)]
    [InlineData(IdentityOperation.Downgrade)]
    public async Task BlocksTwoFactor_WhenDisabled(IdentityOperation op) {
        var gate = Gate<object>(new() { AllowTwoFactorAuthentication = false });
        var ctx  = new AdviceContext(null!);

        await Assert.ThrowsAsync<NotFoundException>(() => gate.AdviseAsync(ctx, null!, op, Anonymous));
    }

    [Theory]
    [InlineData(IdentityOperation.Login)]
    [InlineData(IdentityOperation.Refresh)]
    [InlineData(IdentityOperation.Profile)]
    public async Task AlwaysAllowsLoginRefreshProfile(IdentityOperation op) {
        // These operations have no toggle — they're always allowed
        var opts = new SchemataIdentityOptions {
            AllowRegistration            = false,
            AllowPasswordReset           = false,
            AllowEmailChange             = false,
            AllowPhoneNumberChange       = false,
            AllowPasswordChange          = false,
            AllowAccountConfirmation     = false,
            AllowTwoFactorAuthentication = false,
        };
        var gate = Gate<object>(opts);
        var ctx  = new AdviceContext(null!);

        var result = await gate.AdviseAsync(ctx, null!, op, Anonymous);

        Assert.Equal(AdviseResult.Continue, result);
    }
}
