using System.Security.Claims;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Identity.Foundation.Advisors;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Models;
using Xunit;

namespace Schemata.Identity.Tests.Advisors;

public class AdviceRequestDowngradeValidationShould
{
    private static readonly ClaimsPrincipal Anonymous = new();

    private static IdentityOperationExecution<IdentityResult<Unit>> Execution(
        IdentityOperation operation,
        ClaimsPrincipal?  principal = null
    ) {
        return new() { Operation = operation, Principal = principal ?? Anonymous };
    }

    [Fact]
    public async Task Continues_WhenTwoFactorCodeProvided() {
        var advisor = new AdviceRequestDowngradeValidation();
        var ctx     = new AdviceContext(null!);
        var request = new AuthenticatorRequest { TwoFactorCode = "123456" };

        var result = await advisor.AdviseAsync(ctx, request, Execution(IdentityOperation.Downgrade));

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task Continues_WhenRecoveryCodeProvided() {
        var advisor = new AdviceRequestDowngradeValidation();
        var ctx     = new AdviceContext(null!);
        var request = new AuthenticatorRequest { TwoFactorRecoveryCode = "recovery-code" };

        var result = await advisor.AdviseAsync(ctx, request, Execution(IdentityOperation.Downgrade));

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task ThrowsValidation_WhenBothCodesEmpty() {
        var advisor = new AdviceRequestDowngradeValidation();
        var ctx     = new AdviceContext(null!);
        var request = new AuthenticatorRequest();

        await Assert.ThrowsAsync<ValidationException>(() => advisor.AdviseAsync(
                                                          ctx, request, Execution(IdentityOperation.Downgrade)));
    }

    [Fact]
    public async Task Skips_ForOtherOperations() {
        var advisor = new AdviceRequestDowngradeValidation();
        var ctx     = new AdviceContext(null!);
        var request = new AuthenticatorRequest();

        var result = await advisor.AdviseAsync(ctx, request, Execution(IdentityOperation.Enroll));

        Assert.Equal(AdviseResult.Continue, result);
    }
}
