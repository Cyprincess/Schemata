using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Tests;

public class AdviceCodeExchangePkceShould
{
    // RFC 7636 Appendix B test vectors.
    private const string Verifier      = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
    private const string ChallengeS256 = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

    private static AdviceCodeExchangePkce<SchemataApplication, SchemataToken> CreateAdvisor(bool requireS256 = true, bool downgradeProtection = true) {
        var opts = new CodeFlowOptions {
            RequirePkceS256 = requireS256, RequirePkceDowngradeProtection = downgradeProtection,
        };
        return new AdviceCodeExchangePkce<SchemataApplication, SchemataToken>(Options.Create(opts));
    }

    private static AdviceContext CreateContext() { return new(new ServiceCollection().BuildServiceProvider()); }

    private static CodeExchangeContext<SchemataApplication, SchemataToken> Exchange(TokenRequest request, AuthorizeRequest payload) {
        return new() {
            Request   = request,
            Application = new SchemataApplication { ClientId = "test" },
            CodeToken = new SchemataToken(),
            Payload   = payload,
        };
    }

    [Fact]
    public async Task Continues_WhenS256Challenge_MatchesVerifier() {
        var advisor  = CreateAdvisor();
        var exchange = Exchange(
            new TokenRequest { CodeVerifier = Verifier },
            new AuthorizeRequest { CodeChallenge = ChallengeS256, CodeChallengeMethod = PkceMethods.S256 });

        var result = await advisor.AdviseAsync(CreateContext(), exchange);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task ThrowsInvalidGrant_WhenS256Challenge_DoesNotMatchVerifier() {
        var advisor  = CreateAdvisor();
        var exchange = Exchange(
            new TokenRequest { CodeVerifier = "wrong-verifier" },
            new AuthorizeRequest { CodeChallenge = ChallengeS256, CodeChallengeMethod = PkceMethods.S256 });

        var ex = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(CreateContext(), exchange));
        Assert.Equal(OAuthErrors.InvalidGrant, ex.Code);
    }

    [Fact]
    public async Task ThrowsInvalidGrant_WhenVerifierMissing_AndChallengePresent() {
        var advisor  = CreateAdvisor();
        var exchange = Exchange(
            new TokenRequest(),
            new AuthorizeRequest { CodeChallenge = ChallengeS256, CodeChallengeMethod = PkceMethods.S256 });

        var ex = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(CreateContext(), exchange));
        Assert.Equal(OAuthErrors.InvalidGrant, ex.Code);
    }

    [Fact]
    public async Task Continues_WhenNoChallengeAndNoVerifier() {
        var advisor  = CreateAdvisor();
        var exchange = Exchange(new TokenRequest(), new AuthorizeRequest());

        var result = await advisor.AdviseAsync(CreateContext(), exchange);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task Continues_WhenPlainChallenge_MatchesVerifier() {
        var advisor       = CreateAdvisor(false);
        var plainVerifier = "my-plain-verifier-that-is-at-least-43-chars-long_ok";
        var exchange      = Exchange(
            new TokenRequest { CodeVerifier = plainVerifier },
            new AuthorizeRequest { CodeChallenge = plainVerifier, CodeChallengeMethod = PkceMethods.Plain });

        var result = await advisor.AdviseAsync(CreateContext(), exchange);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task ThrowsInvalidGrant_WhenDowngradeProtection_VerifierWithoutChallenge() {
        var advisor  = CreateAdvisor(downgradeProtection: true);
        var exchange = Exchange(
            new TokenRequest { CodeVerifier = Verifier },
            new AuthorizeRequest());

        var ex = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(CreateContext(), exchange));
        Assert.Equal(OAuthErrors.InvalidGrant, ex.Code);
    }
}
