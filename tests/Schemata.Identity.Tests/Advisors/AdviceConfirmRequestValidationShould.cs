using System.Security.Claims;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Identity.Foundation.Advisors;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Models;
using Xunit;

namespace Schemata.Identity.Tests.Advisors;

public class AdviceConfirmRequestValidationShould
{
    private static readonly ClaimsPrincipal Anonymous = new();

    [Fact]
    public async Task Continues_WhenEmailAndCodeProvided() {
        var advisor = new AdviceConfirmRequestValidation();
        var ctx     = new AdviceContext(null!);
        var request = new ConfirmRequest { EmailAddress = "a@b.c", Code = "123456" };

        var result = await advisor.AdviseAsync(ctx, request, IdentityOperation.Confirm, Anonymous);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task Continues_WhenPhoneAndCodeProvided() {
        var advisor = new AdviceConfirmRequestValidation();
        var ctx     = new AdviceContext(null!);
        var request = new ConfirmRequest { PhoneNumber = "+15551234567", Code = "123456" };

        var result = await advisor.AdviseAsync(ctx, request, IdentityOperation.Confirm, Anonymous);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ThrowsValidation_WhenCodeEmpty(string? code) {
        var advisor = new AdviceConfirmRequestValidation();
        var ctx     = new AdviceContext(null!);
        var request = new ConfirmRequest { EmailAddress = "a@b.c", Code = code! };

        await Assert.ThrowsAsync<ValidationException>(() => advisor.AdviseAsync(
                                                          ctx, request, IdentityOperation.Confirm, Anonymous));
    }

    [Fact]
    public async Task ThrowsValidation_WhenBothContactsEmpty() {
        var advisor = new AdviceConfirmRequestValidation();
        var ctx     = new AdviceContext(null!);
        var request = new ConfirmRequest { Code = "123456" };

        await Assert.ThrowsAsync<ValidationException>(() => advisor.AdviseAsync(
                                                          ctx, request, IdentityOperation.Confirm, Anonymous));
    }

    [Fact]
    public async Task SkipsForOtherOperations() {
        var advisor = new AdviceConfirmRequestValidation();
        var ctx     = new AdviceContext(null!);
        var request = new ConfirmRequest();

        var result = await advisor.AdviseAsync(ctx, request, IdentityOperation.Login, Anonymous);

        Assert.Equal(AdviseResult.Continue, result);
    }
}
