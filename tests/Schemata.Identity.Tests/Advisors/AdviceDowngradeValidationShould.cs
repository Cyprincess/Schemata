using System.Security.Claims;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Identity.Foundation.Advisors;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Models;
using Xunit;

namespace Schemata.Identity.Tests.Advisors;

public class AdviceDowngradeValidationShould
{
    private static readonly ClaimsPrincipal Anonymous = new();

    [Fact]
    public async Task Continues_WhenTwoFactorCodeProvided() {
        var advisor = new AdviceDowngradeValidation();
        var ctx     = new AdviceContext(null!);
        var request = new AuthenticatorRequest { TwoFactorCode = "123456" };

        var result = await advisor.AdviseAsync(ctx, request, IdentityOperation.Downgrade, Anonymous);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task Continues_WhenRecoveryCodeProvided() {
        var advisor = new AdviceDowngradeValidation();
        var ctx     = new AdviceContext(null!);
        var request = new AuthenticatorRequest { TwoFactorRecoveryCode = "recovery-code" };

        var result = await advisor.AdviseAsync(ctx, request, IdentityOperation.Downgrade, Anonymous);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task ThrowsValidation_WhenBothCodesEmpty() {
        var advisor = new AdviceDowngradeValidation();
        var ctx     = new AdviceContext(null!);
        var request = new AuthenticatorRequest();

        await Assert.ThrowsAsync<ValidationException>(() => advisor.AdviseAsync(
                                                          ctx, request, IdentityOperation.Downgrade, Anonymous));
    }

    [Fact]
    public async Task SkipsForOtherOperations() {
        var advisor = new AdviceDowngradeValidation();
        var ctx     = new AdviceContext(null!);
        var request = new AuthenticatorRequest();

        var result = await advisor.AdviseAsync(ctx, request, IdentityOperation.Enroll, Anonymous);

        Assert.Equal(AdviseResult.Continue, result);
    }
}
