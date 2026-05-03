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
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Tests;

public class AdviceAuthorizePkceShould
{
    private static (AdviceAuthorizePkce<SchemataApplication> advisor, AdviceContext ctx,
        AuthorizeContext<SchemataApplication> authz) Create(
            bool  requirePkce    = true,
            bool  requireS256    = true,
            bool? appRequirePkce = null
        ) {
        var opts    = new CodeFlowOptions { RequirePkce = requirePkce, RequirePkceS256 = requireS256 };
        var sp      = new ServiceCollection().BuildServiceProvider();
        var advisor = new AdviceAuthorizePkce<SchemataApplication>(Options.Create(opts));
        var ctx     = new AdviceContext(sp);
        var authz = new AuthorizeContext<SchemataApplication> {
            Application = new() { ClientId = "test", RequirePkce = appRequirePkce },
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
        Assert.Equal(OAuthErrors.InvalidRequest, ex.Code);
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
        authz.Request = new() { CodeChallenge = "some-challenge", CodeChallengeMethod = PkceMethods.Plain };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(ctx, authz));
        Assert.Equal(OAuthErrors.InvalidRequest, ex.Code);
    }

    [Fact]
    public async Task Continues_WhenPlainMethodUsed_AndS256NotRequired() {
        var (advisor, ctx, authz) = Create(true, false);
        authz.Request = new() { CodeChallenge = "some-challenge", CodeChallengeMethod = PkceMethods.Plain };

        var result = await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task ThrowsInvalidRequest_WhenUnsupportedMethod() {
        var (advisor, ctx, authz) = Create(true, false);
        authz.Request             = new() { CodeChallenge = "some-challenge", CodeChallengeMethod = "S512" };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(ctx, authz));
        Assert.Equal(OAuthErrors.InvalidRequest, ex.Code);
    }

    [Fact]
    public async Task DefaultsToPlain_WhenMethodNotSpecified() {
        var (advisor, ctx, authz) = Create(false, false);
        authz.Request             = new() { CodeChallenge = "some-challenge" };

        var result = await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task UsesPerClientOverride() {
        // Global: PKCE not required. Per-client: required.
        var (advisor, ctx, authz) = Create(false, appRequirePkce: true);
        authz.Request             = new();

        var ex = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(ctx, authz));
        Assert.Equal(OAuthErrors.InvalidRequest, ex.Code);
    }
}
