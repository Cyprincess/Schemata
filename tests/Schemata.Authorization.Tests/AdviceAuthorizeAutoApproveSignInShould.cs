using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class AdviceAuthorizeAutoApproveSignInShould
{
    private static (AdviceAuthorizeAutoApproveSignIn<SchemataApplication, SchemataAuthorization> advisor,
        Mock<IAuthorizationManager<SchemataAuthorization>> authzMgr) CreateAdvisor(
            string sessionIdClaimType = "sid",
            IOpSessionService? sessions = null) {
        var opts = Options.Create(new SchemataAuthorizationOptions { SessionIdClaimType = sessionIdClaimType });

        var authzMgr = new Mock<IAuthorizationManager<SchemataAuthorization>>();
        authzMgr.Setup(m => m.CreateAsync(It.IsAny<SchemataAuthorization>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((SchemataAuthorization a, CancellationToken _) => {
                     a.Name = "auth-generated-name";
                     a.CanonicalName = "authorizations/auth-generated-name";
                     return a;
                 });

        return (new(opts, authzMgr.Object, sessions: sessions), authzMgr);
    }

    private static AuthorizeContext<SchemataApplication> CreateGrantedContext(
        string subject  = "users/u-1",
        string scope    = "openid profile",
        string sid      = "sess-1",
        string clientId = "app-1"
    ) {
        var claims = new List<Claim> { new(IdentityClaims.Subject, subject), new("sid", sid) };
        return new() {
            Application     = new() {
                Uid           = Guid.NewGuid(),
                ClientId      = clientId,
                Name          = clientId,
                CanonicalName = $"applications/{clientId}",
            },
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
        var authResult = Assert.IsType<AuthorizationResult>(authz.Result);
        Assert.Equal("authorizations/auth-generated-name", authResult!.Properties![Properties.AuthorizationName]);

        var invocation = Assert.Single(authzMgr.Invocations,
                                       i => i.Method.Name == nameof(IAuthorizationManager<>.CreateAsync));
        var captured = Assert.IsType<SchemataAuthorization>(invocation.Arguments[0]);
        Assert.Equal("applications/app-1", captured.Application);
        Assert.Equal("users/u-1", captured.Subject);
        Assert.Equal("openid profile", captured.Scopes);
        Assert.Equal(TokenStatuses.Valid, captured.Status);
    }

    [Fact]
    public async Task PropagatesSessionId_FromPrincipal() {
        var (advisor, _) = CreateAdvisor();
        var ctx   = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var authz = CreateGrantedContext(sid: "abc-session");

        await advisor.AdviseAsync(ctx, authz);

        var authResult = Assert.IsType<AuthorizationResult>(authz.Result);
        Assert.Equal("abc-session", authResult!.Properties![Properties.SessionId]);
    }

    [Fact]
    public async Task IssuesSession_WhenPrincipalLacksSid_AndSessionServicePresent() {
        var sessions = new Mock<IOpSessionService>();
        sessions.Setup(m => m.IssueAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("issued-sid");
        var (advisor, _) = CreateAdvisor(sessions: sessions.Object);
        var ctx   = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var authz = CreateGrantedContext();
        authz.Principal = new(new ClaimsIdentity([new(IdentityClaims.Subject, "users/u-1")], "test"));

        var result = await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(AdviseResult.Handle, result);
        sessions.Verify(m => m.IssueAsync(authz.Principal, "users/u-1", It.IsAny<CancellationToken>()), Times.Once);
        var authResult = Assert.IsType<AuthorizationResult>(authz.Result);
        Assert.Equal("issued-sid", authResult!.Properties![Properties.SessionId]);
    }

    [Fact]
    public async Task SignsInWithoutSession_WhenNoSessionServiceRegistered() {
        var (advisor, _) = CreateAdvisor();
        var ctx   = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var authz = CreateGrantedContext();
        authz.Principal = new(new ClaimsIdentity([new(IdentityClaims.Subject, "users/u-1")], "test"));

        var result = await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(AdviseResult.Handle, result);
        var authResult = Assert.IsType<AuthorizationResult>(authz.Result);
        Assert.Null(authResult!.Properties![Properties.SessionId]);
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
    public async Task ContinuesToTheInteractionRedirect_WhenSubjectMissing() {
        var (advisor, authzMgr) = CreateAdvisor();
        var ctx   = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var authz = CreateGrantedContext();
        authz.Principal = new(new ClaimsIdentity("test"));

        var result = await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(AdviseResult.Continue, result);
        authzMgr.Verify(m => m.CreateAsync(It.IsAny<SchemataAuthorization>(), It.IsAny<CancellationToken>()),
                        Times.Never);
    }

    [Fact]
    public async Task Publish_Callback_Session_State_From_The_Session_State_Advisor_Salt() {
        var http = new DefaultHttpContext();
        var sessionOptions = Options.Create(new SessionManagementOptions { OpStateCookieName = "opstate" });
        var sessions = new DefaultOpSessionService(
            Options.Create(new SchemataAuthorizationOptions()),
            [new OpSessionStore(new HttpContextAccessor { HttpContext = http }, sessionOptions)]);
        var sessionState = new AdviceAuthorizeSessionState<SchemataApplication>(
            new HttpContextAccessor { HttpContext = http }, sessionOptions, sessions, new());
        var (advisor, _) = CreateAdvisor();
        var ctx   = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var authz = CreateGrantedContext();
        Assert.NotNull(authz.Request);
        authz.Request.RedirectUri  = "https://client.example/callback";
        authz.Request.ResponseType = ResponseTypes.Code;
        authz.Request.GrantProfile = GrantProfiles.OpenIdConnect;

        await sessionState.AdviseAsync(ctx, authz);
        var result = await advisor.AdviseAsync(ctx, authz);
        Assert.Equal(AdviseResult.Handle, result);
        Assert.NotNull(authz.Result);

        var tokens = new Mock<ITokenStore<SchemataToken>>();
        tokens.Setup(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                        It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .ReturnsAsync((SchemataToken token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token);
        var apps = new Mock<IApplicationManager<SchemataApplication>>();
        apps.Setup(a => a.FindByClientIdAsync("app-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchemataApplication { ClientId = "app-1", CanonicalName = "applications/app-1" });
        var options = new SchemataAuthorizationOptions { Issuer = "https://issuer.example" };
        using var provider = new ServiceCollection()
                            .AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = http })
                            .AddSingleton(new SessionStateFormulator())
                            .AddSingleton(sessionOptions)
                            .BuildServiceProvider();
        var service = new AuthorizationSignInService<SchemataApplication>(
            Options.Create(options), Options.Create(new JsonSerializerOptions()),
            TestSecurityKeys.CreateTokenService(options), apps.Object, tokens.Object, provider);

        var issued = await service.IssueAsync(
            authz.Result.Principal!, authz.Result.Properties, AuthorizationSignInResponseKind.Callback);

        // Independent oracle per OpenID Connect Session Management: base64url of SHA-256 over
        // "client_id origin op_browser_state salt", then "." and the salt.
        var opstate = http.Response.Headers.SetCookie
                          .Select(value => value?.ToString() ?? string.Empty)
                          .Last(value => value.StartsWith("opstate=", StringComparison.Ordinal))
                          ["opstate=".Length..].Split(';', 2)[0];
        var salt     = authz.Request.SessionStateSalt!;
        var digest   = SHA256.HashData(Encoding.UTF8.GetBytes($"app-1 https://client.example {opstate} {salt}"));
        var expected = $"{Convert.ToBase64String(digest).TrimEnd('=').Replace('+', '-').Replace('/', '_')}.{salt}";
        Assert.NotNull(issued.Callback);
        Assert.Equal(expected, issued.Callback.Parameters[Parameters.SessionState]);
        Assert.False(string.IsNullOrWhiteSpace(issued.Callback.Parameters[Parameters.Code]));
        // The post-login OP state the user agent compares against carries the same value.
        Assert.True(SessionStateContext.TryGet(http, out var current));
        Assert.Equal(expected, current);
    }
}
