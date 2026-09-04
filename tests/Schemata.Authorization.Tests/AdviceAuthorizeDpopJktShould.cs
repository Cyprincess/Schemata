using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class AdviceAuthorizeDpopJktShould
{
    /// <summary>The dpop_jkt example value from RFC 9449 §10 Figure 25.</summary>
    private const string Thumbprint = "NzbLsXh8uDCcd-6MNwXF4W_7noWXFZAfHkxZsRGC9Xs";

    [Fact]
    public async Task Continue_With_The_Committed_Key_Recorded_On_The_Request() {
        var (advisor, ctx, authz) = Create(Thumbprint);

        var result = await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Equal(Thumbprint, authz.Request!.DpopJkt);
    }
    [Fact]
    public async Task Continue_When_The_Parameter_Is_Absent() {
        var (advisor, ctx, authz) = Create(null);

        var result = await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(AdviseResult.Continue, result);
    }
    [Theory]
    [InlineData("not-a-thumbprint!")]
    [InlineData("a+b/c=d")]
    public async Task Reject_A_Parameter_Outside_The_Base64url_Alphabet(string jkt) {
        var (advisor, ctx, authz) = Create(jkt);

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            advisor.AdviseAsync(ctx, authz));

        Assert.Equal(OAuthErrors.InvalidRequest, exception.Status);
        Assert.Equal(400, exception.Code);
    }
    [Theory]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]   // decodes to 24 bytes
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // decodes to 28 bytes
    public async Task Reject_A_Parameter_That_Does_Not_Decode_To_32_Bytes(string jkt) {
        var (advisor, ctx, authz) = Create(jkt);

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            advisor.AdviseAsync(ctx, authz));

        Assert.Equal(OAuthErrors.InvalidRequest, exception.Status);
        Assert.Equal(400, exception.Code);
    }

    private static (
        AdviceAuthorizeDpopJkt<SchemataApplication> Advisor,
        AdviceContext Ctx,
        AuthorizeContext<SchemataApplication> Authz
    ) Create(string? dpopJkt) {
        var ctx   = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var authz = new AuthorizeContext<SchemataApplication> {
            Request = new() { DpopJkt = dpopJkt },
        };
        return (new(), ctx, authz);
    }
}
