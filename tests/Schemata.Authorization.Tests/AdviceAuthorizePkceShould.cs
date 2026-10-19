using Schemata.Abstractions;
using Schemata.Abstractions.Errors;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class AdviceAuthorizePkceShould
{
    private const string Challenge = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQ";

    private static (AdviceAuthorizePkce<SchemataApplication> advisor, AdviceContext ctx,
        AuthorizeContext<SchemataApplication> authz) Create(
            bool requirePkce = true,
            bool requireS256 = true
        ) {
        var opts    = new CodeFlowOptions { RequirePkce = requirePkce, RequirePkceS256 = requireS256 };
        var sp      = new ServiceCollection().BuildServiceProvider();
        var advisor = new AdviceAuthorizePkce<SchemataApplication>(Options.Create(opts));
        var ctx     = new AdviceContext(sp);
        var authz = new AuthorizeContext<SchemataApplication> {
            Application = new() { ClientId = "test" },
        };
        return (advisor, ctx, authz);
    }

    [Fact]
    public async Task Continues_WhenPkceNotRequired_AndNoChallengeProvided() {
        var (advisor, ctx, authz) = Create(false);
        authz.Request             = new();

        var result = await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task ThrowsInvalidRequest_WhenPkceRequired_AndNoChallengeProvided() {
        var (advisor, ctx, authz) = Create();
        authz.Request             = new();

        var ex = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(ctx, authz));
        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
        var info = ErrorInfo(ex);
        Assert.Equal(SchemataResources.NOT_EMPTY, info.Reason);
        Assert.Equal(Parameters.CodeChallenge, info.Metadata?["value"]);
    }

    [Fact]
    public async Task Continues_WhenPkceRequired_AndChallengeProvided_S256() {
        var (advisor, ctx, authz) = Create();
        authz.Request = new() {
            CodeChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", CodeChallengeMethod = PkceMethods.S256,
        };

        var result = await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task ThrowsInvalidRequest_WhenPlainMethodUsed_AndS256Required() {
        var (advisor, ctx, authz) = Create();
        authz.Request             = new() { CodeChallenge = Challenge, CodeChallengeMethod = PkceMethods.Plain };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(ctx, authz));
        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
        var info = ErrorInfo(ex);
        Assert.Equal(SchemataResources.CODE_CHALLENGE_METHOD_NOT_ALLOWED, info.Reason);
        Assert.Equal(PkceMethods.Plain, info.Metadata?["value"]);
    }

    [Fact]
    public async Task Continues_WhenPlainMethodUsed_AndS256NotRequired() {
        var (advisor, ctx, authz) = Create(true, false);
        authz.Request             = new() { CodeChallenge = Challenge, CodeChallengeMethod = PkceMethods.Plain };

        var result = await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task ThrowsInvalidRequest_WhenUnsupportedMethod() {
        var (advisor, ctx, authz) = Create(true, false);
        authz.Request             = new() { CodeChallenge = Challenge, CodeChallengeMethod = "S512" };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(ctx, authz));
        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
        var info = ErrorInfo(ex);
        Assert.Equal(SchemataResources.NOT_SUPPORTED, info.Reason);
        Assert.Equal("S512", info.Metadata?["value"]);
    }

    [Fact]
    public async Task ThrowsInvalidRequest_WhenChallengeMalformed() {
        var (advisor, ctx, authz) = Create(true, false);
        authz.Request             = new() { CodeChallenge = "too-short", CodeChallengeMethod = PkceMethods.Plain };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(ctx, authz));
        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
    }

    [Fact]
    public async Task DefaultsToPlain_WhenMethodNotSpecified() {
        var (advisor, ctx, authz) = Create(false, false);
        authz.Request             = new() { CodeChallenge = Challenge };

        var result = await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(AdviseResult.Continue, result);
    }


    private static ErrorInfoDetail ErrorInfo(OAuthException ex) {
        return Assert.Single(ex.Details!.OfType<ErrorInfoDetail>());
    }

    

    [Theory]
    [InlineData(42)]
    [InlineData(129)]
    public async Task ReportsGrammarFact_WhenChallengeLengthViolatesRfc7636(int length) {
        var (advisor, ctx, authz) = Create();
        authz.Request             = new() {
            CodeChallenge = new('a', length), CodeChallengeMethod = PkceMethods.S256,
        };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(ctx, authz));

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
        Assert.Equal(SchemataResources.CODE_CHALLENGE_INVALID, ErrorInfo(ex).Reason);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOP!")]
    [InlineData("abcdefghijklmnopqrstuvwxyzABCDEFGH中文OPQ")]
    public async Task ReportsGrammarFact_WhenChallengePresentButMalformed(string challenge) {
        var (advisor, ctx, authz) = Create();
        authz.Request             = new() { CodeChallenge = challenge, CodeChallengeMethod = PkceMethods.S256 };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(ctx, authz));

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
        Assert.Equal(SchemataResources.CODE_CHALLENGE_INVALID, ErrorInfo(ex).Reason);
    }

    [Fact]
    public async Task Continues_WhenChallengeLengthIsUpperBoundary() {
        var (advisor, ctx, authz) = Create();
        authz.Request = new() {
            CodeChallenge = new('a', 128), CodeChallengeMethod = PkceMethods.S256,
        };

        var result = await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(AdviseResult.Continue, result);
    }

    

    

    [Fact]
    public async Task NeverIncludesChallengeSecret_InDiagnosticMetadata() {
        var (advisor, ctx, authz) = Create();
        var secret                = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOP!";
        authz.Request             = new() { CodeChallenge = secret, CodeChallengeMethod = PkceMethods.S256 };
        var ex = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(ctx, authz));

        var info = ErrorInfo(ex);
        Assert.All(info.Metadata?.Values ?? Enumerable.Empty<string>(), value => Assert.DoesNotContain(secret, value));
    }
}
