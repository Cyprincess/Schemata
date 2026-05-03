using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Tests;

public class AdviceAuthorizeAutoApproveSignInShould
{
    private static (AdviceAuthorizeAutoApproveSignIn<SchemataApplication, SchemataAuthorization> advisor,
        Mock<IAuthorizationManager<SchemataAuthorization>> authzMgr) CreateAdvisor(string sessionIdClaimType = "sid") {
        var opts = Options.Create(new SchemataAuthorizationOptions { SessionIdClaimType = sessionIdClaimType });

        var authzMgr = new Mock<IAuthorizationManager<SchemataAuthorization>>();
        authzMgr.Setup(m => m.CreateAsync(It.IsAny<SchemataAuthorization>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((SchemataAuthorization a, CancellationToken _) => {
                     a.Name = "auth-generated-name";
                     return a;
                 });

        return (new(opts, authzMgr.Object), authzMgr);
    }

    private static AuthorizeContext<SchemataApplication> CreateGrantedContext(
        string subject  = "user-1",
        string scope    = "openid profile",
        string sid      = "sess-1",
        string clientId = "app-1"
    ) {
        var claims = new List<Claim> { new(Claims.Subject, subject), new("sid", sid) };
        // ClientId and Name are aliased on SchemataApplication; setting ClientId last.
        return new() {
            Application     = new() { Id    = 1, ClientId = clientId },
            Request         = new() { Scope = scope },
            Principal       = new(new ClaimsIdentity(claims, "test")),
            ConsentDecision = ConsentDecision.Granted,
        };
    }

    [Fact]
    public async Task CreatesAuthorizationRecord_AndPropagatesAuthorizationName_WhenGranted() {
        var (advisor, authzMgr) = CreateAdvisor();
        var ctx   = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var authz = CreateGrantedContext();

        var result = await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(AdviseResult.Handle, result);
        Assert.True(ctx.TryGet<AuthorizationResult>(out var authResult));
        Assert.Equal("auth-generated-name", authResult!.Properties![Properties.AuthorizationName]);

        var invocation = Assert.Single(authzMgr.Invocations,
                                       i => i.Method.Name
                                         == nameof(IAuthorizationManager<SchemataAuthorization>.CreateAsync));
        var captured = Assert.IsType<SchemataAuthorization>(invocation.Arguments[0]);
        Assert.Equal("app-1", captured.ApplicationName);
        Assert.Equal("user-1", captured.Subject);
        Assert.Equal("openid profile", captured.Scopes);
        Assert.Equal(TokenStatuses.Valid, captured.Status);
    }

    [Fact]
    public async Task PropagatesSessionId_FromPrincipal() {
        var (advisor, _) = CreateAdvisor();
        var ctx   = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var authz = CreateGrantedContext(sid: "abc-session");

        await advisor.AdviseAsync(ctx, authz);

        Assert.True(ctx.TryGet<AuthorizationResult>(out var authResult));
        Assert.Equal("abc-session", authResult!.Properties![Properties.SessionId]);
    }

    [Fact]
    public async Task PassesThrough_WhenConsentNotGranted() {
        var (advisor, authzMgr) = CreateAdvisor();
        var ctx   = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var authz = CreateGrantedContext();
        authz.ConsentDecision = ConsentDecision.Denied;

        var result = await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(AdviseResult.Continue, result);
        authzMgr.Verify(m => m.CreateAsync(It.IsAny<SchemataAuthorization>(), It.IsAny<CancellationToken>()),
                        Times.Never);
    }

    [Fact]
    public async Task PassesThrough_WhenReauthenticationRequired() {
        var (advisor, authzMgr) = CreateAdvisor();
        var ctx   = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var authz = CreateGrantedContext();
        authz.RequireReauthentication = true;

        var result = await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(AdviseResult.Continue, result);
        authzMgr.Verify(m => m.CreateAsync(It.IsAny<SchemataAuthorization>(), It.IsAny<CancellationToken>()),
                        Times.Never);
    }

    [Fact]
    public async Task ThrowsLoginRequired_WhenSubjectMissing() {
        var (advisor, _) = CreateAdvisor();
        var ctx   = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var authz = CreateGrantedContext();
        authz.Principal = new(new ClaimsIdentity("test"));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(ctx, authz));
        Assert.Equal(OAuthErrors.LoginRequired, ex.Code);
    }
}
