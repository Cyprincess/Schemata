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
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Filters;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;

namespace Schemata.Authorization.Tests;

public class SessionStateShould
{
    [Fact]
    public async Task Discover_Logout_Participants_Only_From_Participation_Facts() {
        var tokens = new Mock<ITokenStore<SchemataToken>>();
        tokens.Setup(value => value.ListParticipantsAsync("users/user-1", "sid-1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(["applications/expired-client"]);

        var clients = await LogoutSessionHelper.GetSessionClientsAsync(
            tokens.Object, "users/user-1", "sid-1", CancellationToken.None);

        Assert.Equal("applications/expired-client", Assert.Single(clients));
        tokens.Verify(value => value.ListBySessionAsync(
                          It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        tokens.Verify(value => value.ListByParentAsync(
                          It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }


    [Fact]
    public void Build_The_Exact_Sha256_Base64Url_Session_State_Without_Spaces() {
        const string client  = "client-1";
        const string origin  = "https://client.example";
        const string opstate = "opstate-1";
        const string salt    = "0011223344556677";
        var digest = SHA256.HashData(Encoding.ASCII.GetBytes(
                                         "client-1 https://client.example opstate-1 0011223344556677"));
        var expected = $"{Convert.ToBase64String(digest).TrimEnd('=').Replace('+', '-').Replace('/', '_')}.{salt}";
        var subject  = new SessionStateFormulator();

        var first  = subject.Build(client, origin, opstate, salt);
        var second = subject.Build(client, origin, opstate, "8899AABBCCDDEEFF");

        Assert.Equal(expected, first);
        Assert.DoesNotContain(" ", first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Publish_Session_State_For_The_Redirect_Origin_And_Write_A_Public_Opstate_Cookie() {
        var http           = new DefaultHttpContext();
        var sessionOptions = Options.Create(new SessionManagementOptions { OpStateCookieName = "opstate" });
        var sessions = new DefaultOpSessionService(
            Options.Create(new SchemataAuthorizationOptions()),
            [new OpSessionStore(new HttpContextAccessor { HttpContext = http }, sessionOptions)]);
        var advisor = new AdviceAuthorizeSessionState<SchemataApplication>(
            new HttpContextAccessor { HttpContext = http }, sessionOptions, sessions, new());
        using var provider = new ServiceCollection().BuildServiceProvider();
        var       ctx      = new AdviceContext(provider);
        var authz = new AuthorizeContext<SchemataApplication> {
            Application = new() { ClientId = "client-1" },
            Request     = new() {
                RedirectUri  = "https://client.example/callback?x=1",
                GrantProfile = AuthorizationConstants.GrantProfiles.OpenIdConnect,
            },
        };

        await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(AuthorizationConstants.ResponseModes.Query, authz.ResponseMode);
        var cookies = http.Response.Headers.SetCookie.Select(value => value?.ToString() ?? string.Empty).ToArray();
        var cookie  = Assert.Single(cookies, value => value?.StartsWith("opstate=", StringComparison.Ordinal) == true);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=none", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        // The initial context value serves same-request error callbacks from the opstate the
        // response cookie carries; the success callback recomputes it after sign-in.
        var opstate = cookie["opstate=".Length..].Split(';', 2)[0];
        Assert.True(SessionStateContext.TryGet(http, out var initial));
        Assert.Equal(new SessionStateFormulator().Build("client-1", "https://client.example", opstate, authz.SessionStateSalt!), initial);
    }

    [Fact]
    public async Task Skip_Session_State_During_Par_Endpoint_Validation() {
        var http = new DefaultHttpContext();
        var advisor = new AdviceAuthorizeSessionState<SchemataApplication>(
            new HttpContextAccessor { HttpContext = http },
            Options.Create(new SessionManagementOptions()),
            new DefaultOpSessionService(Options.Create(new SchemataAuthorizationOptions())),
            new());
        using var provider = new ServiceCollection().BuildServiceProvider();
        var       ctx      = new AdviceContext(provider);
        var authz = new AuthorizeContext<SchemataApplication> {
            Stage = AuthorizationRequestStage.Pushed,
            Application = new() { ClientId = "client-1" },
            Request     = new() {
                RedirectUri  = "https://client.example/callback",
                GrantProfile = AuthorizationConstants.GrantProfiles.OpenIdConnect,
            },
        };

        await advisor.AdviseAsync(ctx, authz);

        Assert.True(string.IsNullOrEmpty(http.Response.Headers.SetCookie));
        Assert.Null(authz.ResponseMode);
        Assert.Null(authz.Request.SessionStateSalt);
    }

    [Fact]
    public async Task Write_Separate_Public_Opstate_And_HttpOnly_Session_Cookies() {
        var http    = new DefaultHttpContext();
        var service = OpSessions(http);

        var principal = new ClaimsPrincipal(new ClaimsIdentity("cookie"));
        var sid       = await service.IssueAsync(principal, "user-1");

        Assert.False(string.IsNullOrWhiteSpace(sid));
        var cookies = http.Response.Headers.SetCookie.Select(value => value?.ToString() ?? string.Empty).ToArray();
        Assert.Equal(2, cookies.Length);
        var opstate = Assert.Single(cookies, value => value?.StartsWith("opstate=", StringComparison.Ordinal) == true);
        var sidCookie = Assert.Single(cookies, value => value?.StartsWith("opstate.sid=", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(sid, opstate, StringComparison.Ordinal);
        Assert.DoesNotContain("httponly", opstate, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", opstate, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=none", opstate, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", sidCookie, StringComparison.OrdinalIgnoreCase);
        // The mirror cookie binds the sid to the subject it was persisted for.
        Assert.Contains($"opstate.sid={sid}", sidCookie, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Avoid_SetCookie_Churn_When_The_Session_And_Cookies_Are_Unchanged() {
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = "opstate=state-1; opstate.sid=sid-1|dXNlci0x";
        var service   = OpSessions(http);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new("sid", "sid-1")], "cookie"));

        var sid = await service.IssueAsync(principal, "user-1");

        Assert.Equal("sid-1", sid);
        Assert.True(string.IsNullOrEmpty(http.Response.Headers.SetCookie));
    }

    [Fact]
    public async Task Terminate_Host_Session_Then_Rotate_Opstate_When_Invalidated() {
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = "opstate=state-old; opstate.sid=sid-1";
        var host = new Mock<IOpSessionStore>();
        host.SetupGet(s => s.Order).Returns(0);
        var service = OpSessions(http, host.Object);

        await service.InvalidateAsync(null, "user-1", "sid-1");

        var cookies = http.Response.Headers.SetCookie.Select(value => value?.ToString() ?? string.Empty).ToArray();
        var opstate = Assert.Single(cookies, value => value?.StartsWith("opstate=", StringComparison.Ordinal) == true);
        var deletion = Assert.Single(cookies, value => value?.StartsWith("opstate.sid=", StringComparison.Ordinal) == true);
        Assert.DoesNotContain("opstate=state-old", opstate, StringComparison.Ordinal);
        Assert.Contains("opstate.sid=", deletion, StringComparison.Ordinal);
        Assert.Contains("expires=", deletion, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", deletion, StringComparison.OrdinalIgnoreCase);
        host.Verify(
            value => value.ClearAsync("sid-1", null, "user-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Delegate_Default_Session_Invalidation_To_The_Host_Terminator() {
        var host = new Mock<IOpSessionStore>();
        host.SetupGet(s => s.Order).Returns(0);
        var service = new DefaultOpSessionService(Options.Create(new SchemataAuthorizationOptions()), [host.Object]);

        await service.InvalidateAsync(null, "user-1", "sid-1");

        host.Verify(value => value.ClearAsync("sid-1", null, "user-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Clear_Identity_Ticket_Only_When_It_Owns_The_Selected_Target() {
        var http           = new DefaultHttpContext();
        var authentication = new Mock<Microsoft.AspNetCore.Authentication.IAuthenticationService>(MockBehavior.Strict);
        var ticket = new ClaimsPrincipal(new ClaimsIdentity([
            new(SchemataConstants.IdentityClaims.Subject, "user-1"),
            new("sid", "sid-1"),
        ], IdentityConstants.ApplicationScheme));
        authentication.Setup(value => value.AuthenticateAsync(http, IdentityConstants.ApplicationScheme))
                      .ReturnsAsync(Microsoft.AspNetCore.Authentication.AuthenticateResult.Success(
                                        new(
                                            ticket, IdentityConstants.ApplicationScheme)));
        authentication.Setup(value => value.SignOutAsync(
                                 http, IdentityConstants.ApplicationScheme,
                                 It.IsAny<Microsoft.AspNetCore.Authentication.AuthenticationProperties?>()))
                      .Returns(Task.CompletedTask);
        http.RequestServices = new ServiceCollection()
                              .AddSingleton(authentication.Object)
                              .BuildServiceProvider();
        var store = new Identity.IdentityHostSessionStore(
            new HttpContextAccessor { HttpContext = http },
            Options.Create(new SchemataAuthorizationOptions { SessionIdClaimType = "sid" }), new Mock<ITokenStore<SchemataToken>>().Object);

        await store.ClearAsync("sid-1", new(new ClaimsIdentity("other")), "user-1");

        authentication.Verify(value => value.SignOutAsync(
                                  http, IdentityConstants.ApplicationScheme,
                                  It.IsAny<Microsoft.AspNetCore.Authentication.AuthenticationProperties?>()), Times.Once);
    }

    [Fact]
    public async Task Preserve_Identity_Ticket_When_It_Does_Not_Own_The_Selected_Target() {
        var http           = new DefaultHttpContext();
        var authentication = new Mock<Microsoft.AspNetCore.Authentication.IAuthenticationService>(MockBehavior.Strict);
        var ticket = new ClaimsPrincipal(new ClaimsIdentity([
            new(SchemataConstants.IdentityClaims.Subject, "user-1"),
            new("sid", "sid-current"),
        ], IdentityConstants.ApplicationScheme));
        authentication.Setup(value => value.AuthenticateAsync(http, IdentityConstants.ApplicationScheme))
                      .ReturnsAsync(Microsoft.AspNetCore.Authentication.AuthenticateResult.Success(
                                        new(
                                            ticket, IdentityConstants.ApplicationScheme)));
        http.RequestServices = new ServiceCollection()
                              .AddSingleton(authentication.Object)
                              .BuildServiceProvider();
        var store = new Identity.IdentityHostSessionStore(
            new HttpContextAccessor { HttpContext = http },
            Options.Create(new SchemataAuthorizationOptions { SessionIdClaimType = "sid" }), new Mock<ITokenStore<SchemataToken>>().Object);

        await store.ClearAsync("sid-other", ticket, "user-1");

        authentication.Verify(value => value.SignOutAsync(
                                  It.IsAny<HttpContext>(), It.IsAny<string>(),
                                  It.IsAny<Microsoft.AspNetCore.Authentication.AuthenticationProperties?>()), Times.Never);
    }

    [Fact]
    public async Task Not_Mint_A_Session_Identifier_For_An_Anonymous_Request() {
        var service = new DefaultOpSessionService(Options.Create(new SchemataAuthorizationOptions()));

        // Issue #90: an anonymous request — no host ticket, no synthetic bearer — never
        // establishes an OP session.
        var sid = await service.IssueAsync(null, "user-1");

        Assert.Null(sid);
    }

    [Fact]
    public async Task Reject_Conflicting_Trusted_Session_Evidence() {
        var principal    = new ClaimsPrincipal(new ClaimsIdentity([new("sid", "sid-claim-1")], "cookie"));
        var continuation = new EvidenceStore(new("user-1", "sid-grant-1", OpSessionProvenance.Continuation));
        var service = new DefaultOpSessionService(
            Options.Create(new SchemataAuthorizationOptions { SessionIdClaimType = "sid" }), [continuation]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.IssueAsync(principal, "user-1"));
    }

    [Fact]
    public async Task Reconcile_A_Divergent_Browser_Mirror_To_The_Trusted_Host_Ticket() {
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = "opstate=state-1; opstate.sid=sid-mirror";
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new("sid", "sid-claim-1")], "cookie"));
        var service   = OpSessions(http);

        var sid = await service.IssueAsync(principal, "user-1");

        Assert.Equal("sid-claim-1", sid);
        Assert.Contains($"opstate.sid={sid}", http.Response.Headers.SetCookie.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reuse_The_Browser_Mirror_For_The_Same_Authenticated_Subject() {
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = "opstate=state-1; opstate.sid=sid-mirror|dXNlci0x";
        var service   = OpSessions(http);
        var principal = new ClaimsPrincipal(new ClaimsIdentity("cookie"));

        var sid = await service.IssueAsync(principal, "user-1");

        Assert.Equal("sid-mirror", sid);
    }

    [Fact]
    public async Task Not_Reuse_The_Browser_Mirror_Of_Another_Subject_On_An_Account_Switch() {
        var previous = "sid-old|" + Convert.ToBase64String(Encoding.UTF8.GetBytes("user-a"));
        var http     = new DefaultHttpContext();
        http.Request.Headers.Cookie = "opstate=state-1; opstate.sid=" + previous;
        var service   = OpSessions(http);
        var principal = new ClaimsPrincipal(new ClaimsIdentity("cookie"));

        // Account A's mirror is not account B's session: the switch mints a fresh observable
        // session instead of continuing A's identifier.
        var sid = await service.IssueAsync(principal, "user-b");

        Assert.Matches("^[0-9A-F]{32}$", sid);
        Assert.NotEqual("sid-old", sid);
    }

    [Fact]
    public async Task Aggregate_Clear_Failures_And_Clear_The_Remaining_Adapters() {
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = "opstate=state-old; opstate.sid=sid-1";
        var failing = new ThrowingStore();
        var service = OpSessions(http, failing);

        var ex = await Record.ExceptionAsync(() => service.InvalidateAsync(null, "user-1", "sid-1"));

        var aggregate = Assert.IsType<AggregateException>(ex);
        Assert.IsType<InvalidOperationException>(Assert.Single(aggregate.InnerExceptions));
        Assert.True(failing.Cleared);
        var deletion = http.Response.Headers.SetCookie.ToString();
        Assert.Contains("opstate.sid=", deletion, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clear_The_Host_Session_Even_When_No_Identifier_Exists() {
        var host = new Mock<IOpSessionStore>();
        host.SetupGet(s => s.Order).Returns(0);
        var service = new DefaultOpSessionService(Options.Create(new SchemataAuthorizationOptions()), [host.Object]);

        await service.InvalidateAsync(null, "user-1", null);

        host.Verify(value => value.ClearAsync(null, null, "user-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Not_Reuse_The_Incoming_Cookie_After_The_Same_Request_Cleared_It() {
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = "opstate=state-old; opstate.sid=sid-1";
        var service = OpSessions(http);

        await service.InvalidateAsync(null, "user-1", "sid-1");

        var sid = await service.IssueAsync(null, "user-1");

        Assert.NotEqual("sid-1", sid);
        Assert.Contains($"opstate.sid={sid}", http.Response.Headers.SetCookie.ToString(), StringComparison.Ordinal);
    }

    private sealed class EvidenceStore(OpSessionEvidence? evidence) : IOpSessionStore
    {
        public string? Persisted { get; private set; }

        public int Order => 0;

        public Task<OpSessionEvidence?> ReadAsync(ClaimsPrincipal? principal, string? subject, CancellationToken ct = default) {
            return Task.FromResult(evidence);
        }

        public Task<LogoutSessionEvidence?> ReadLogoutAsync(
            LogoutSessionTarget target, ClaimsPrincipal? principal, CancellationToken ct = default) {
            return Task.FromResult<LogoutSessionEvidence?>(null);
        }

        public Task PersistAsync(string sessionId, ClaimsPrincipal? principal, string? subject, CancellationToken ct = default) {
            Persisted = sessionId;
            return Task.CompletedTask;
        }

        public Task ClearAsync(string? sessionId, ClaimsPrincipal? principal, string? subject, CancellationToken ct = default) {
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingStore : IOpSessionStore
    {
        public bool Cleared { get; private set; }

        public int Order => 50;

        public Task<OpSessionEvidence?> ReadAsync(ClaimsPrincipal? principal, string? subject, CancellationToken ct = default) {
            return Task.FromResult<OpSessionEvidence?>(null);
        }

        public Task<LogoutSessionEvidence?> ReadLogoutAsync(
            LogoutSessionTarget target, ClaimsPrincipal? principal, CancellationToken ct = default) {
            return Task.FromResult<LogoutSessionEvidence?>(null);
        }

        public Task PersistAsync(string sessionId, ClaimsPrincipal? principal, string? subject, CancellationToken ct = default) {
            return Task.CompletedTask;
        }

        public Task ClearAsync(string? sessionId, ClaimsPrincipal? principal, string? subject, CancellationToken ct = default) {
            Cleared = true;
            throw new InvalidOperationException("clear failed");
        }
    }

    [Fact]
    public async Task Mint_Only_Under_An_Authenticated_Basis_When_The_Browser_Channel_Has_No_Request() {
        var service = new DefaultOpSessionService(
            Options.Create(new SchemataAuthorizationOptions()),
            [new OpSessionStore(new HttpContextAccessor(), Options.Create(new SessionManagementOptions()))]);

        var anonymous = await service.IssueAsync(null, "user-1");
        Assert.Null(anonymous);

        var principal = new ClaimsPrincipal(new ClaimsIdentity("cookie"));
        var sid       = await service.IssueAsync(principal, "user-1");
        Assert.Matches("^[0-9A-F]{32}$", sid);
    }

    [Fact]
    public async Task Prefer_The_Principal_Sid_Claim_Over_Minting_Without_A_Browser_Channel() {
        var service   = new DefaultOpSessionService(Options.Create(new SchemataAuthorizationOptions()));
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new("sid", "sid-claim-1")], "cookie"));

        var sid = await service.IssueAsync(principal, "user-1");

        Assert.Equal("sid-claim-1", sid);
    }

    [Fact]
    public async Task Render_A_Client_Scoped_Origin_Check_Using_WebCrypto_Base64Url() {
        var apps = new Mock<IApplicationManager<SchemataApplication>>();
        apps.SetupTypedMetadata();
        apps.Setup(a => a.ListAsync(null, It.IsAny<CancellationToken>())).Returns(Enumerate(
                                                                                      new SchemataApplication {
                                                                                          ClientId = "native-client",
                                                                                          RedirectUris = ["https://client.example/callback", "https://client.example/other"],
                                                                                      },
                                                                                      new SchemataApplication {
                                                                                          ClientId = "other-client",
                                                                                          RedirectUris = ["https://other.example/callback"],
                                                                                      }));
        var handler = new SessionManagementHandler<SchemataApplication>(
            apps.Object, Options.Create(new SessionManagementOptions { OpStateCookieName = "opstate" }));

        var html = await handler.CheckSessionAsync(CancellationToken.None);

        Assert.Contains("\"native-client\":[\"https://client.example\"]", html, StringComparison.Ordinal);
        Assert.Contains("\"other-client\":[\"https://other.example\"]", html, StringComparison.Ordinal);
        Assert.Contains("crypto.subtle.digest('SHA-256'", html, StringComparison.Ordinal);
        Assert.Contains("btoa(s).replace(/\\+/g,'-').replace(/\\//g,'_').replace(/=+$/,'')", html,
                        StringComparison.Ordinal);
        Assert.Contains("const allowed=ORIGINS[p[0]]", html, StringComparison.Ordinal);
        Assert.Contains("allowed.includes(e.origin)", html, StringComparison.Ordinal);
        Assert.Contains("postMessage(v,e.origin)", html, StringComparison.Ordinal);
        Assert.DoesNotContain("postMessage(v,'*')", html, StringComparison.Ordinal);
        Assert.DoesNotContain("||true", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Carry_Session_State_Into_A_Redirect_Error_Response() {
        var http      = new DefaultHttpContext();
        var action    = new ActionContext(http, new(), new());
        var exception = OAuthException.FromDescription(AuthorizationConstants.OAuthErrors.InvalidRequest, "invalid request");
        exception.RedirectUri  = "https://client.example/callback";
        exception.ResponseMode = AuthorizationConstants.ResponseModes.Query;
        var context = new ExceptionContext(action, new List<IFilterMetadata>()) { Exception = exception };
        SessionStateContext.Set(http, "session-value.salt");
        var filter = new OAuthExceptionFilter(Options.Create(new SchemataAuthorizationOptions()));

        filter.OnException(context);

        var redirect = Assert.IsType<RedirectResult>(context.Result);
        Assert.Contains("session_state=session-value.salt", redirect.Url, StringComparison.Ordinal);
        Assert.Contains("error=invalid_request", redirect.Url, StringComparison.Ordinal);
        Assert.True(context.ExceptionHandled);
    }

    [Fact]
    public async Task Carry_Session_State_Into_A_Successful_Authorization_Callback() {
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = "opstate=state-1";
        using var provider = SessionStateProvider(http);
        var       options  = new SchemataAuthorizationOptions { Issuer = "https://issuer.example" };
        var       tokens   = new Mock<ITokenStore<SchemataToken>>();
        tokens.Setup(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .ReturnsAsync((SchemataToken token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token);
        var apps = new Mock<IApplicationManager<SchemataApplication>>();
        apps.Setup(a => a.FindByClientIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchemataApplication { ClientId = "client-1", CanonicalName = "applications/client-1" });
        var service = new AuthorizationSignInService<SchemataApplication>(
            Options.Create(options),
            Options.Create(new JsonSerializerOptions()),
            TestSecurityKeys.CreateTokenService(options),
            apps.Object,
            tokens.Object,
            provider);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(SchemataConstants.IdentityClaims.Subject, "user-1"),
            new(AuthorizationConstants.Claims.ClientId, "client-1"),
        ], "grant"));

        var response = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [AuthorizationConstants.Properties.ResponseType]     = AuthorizationConstants.ResponseTypes.Code,
            [AuthorizationConstants.Properties.RedirectUri]      = "https://client.example/callback",
            [AuthorizationConstants.Properties.ResponseMode]     = AuthorizationConstants.ResponseModes.Query,
            [AuthorizationConstants.Properties.Scope]            = AuthorizationConstants.Scopes.OpenId,
            [AuthorizationConstants.Properties.GrantProfile]     = AuthorizationConstants.GrantProfiles.OpenIdConnect,
            [AuthorizationConstants.Properties.SessionStateSalt] = "0011223344556677",
        }, AuthorizationSignInResponseKind.Callback);

        var expected = new SessionStateFormulator().Build(
            "client-1", "https://client.example", "state-1", "0011223344556677");
        Assert.NotNull(response.Callback);
        Assert.Equal(expected, response.Callback.Parameters[AuthorizationConstants.Parameters.SessionState]);
        Assert.False(string.IsNullOrWhiteSpace(response.Callback.Parameters[AuthorizationConstants.Parameters.Code]));
        // The recomputed value replaces the request-local value for error callbacks.
        Assert.True(SessionStateContext.TryGet(http, out var current));
        Assert.Equal(expected, current);
    }

    [Fact]
    public async Task Recompute_From_The_Final_Opstate_After_Sign_In() {
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = "opstate=state-pre-login";
        using var provider = SessionStateProvider(http);
        var       options  = new SchemataAuthorizationOptions { Issuer = "https://issuer.example" };
        var       tokens   = new Mock<ITokenStore<SchemataToken>>();
        tokens.Setup(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .ReturnsAsync((SchemataToken token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token);
        var apps = new Mock<IApplicationManager<SchemataApplication>>();
        apps.Setup(a => a.FindByClientIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchemataApplication { ClientId = "client-1", CanonicalName = "applications/client-1" });
        string? rotated  = null;
        var     sessions = new Mock<IOpSessionService>();
        sessions.Setup(s => s.IssueAsync(
                           It.IsAny<ClaimsPrincipal>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => {
                     rotated = OpState.Rotate(http, "opstate");
                     return "sid-1";
                 });
        var service = new AuthorizationSignInService<SchemataApplication>(
            Options.Create(options),
            Options.Create(new JsonSerializerOptions()),
            TestSecurityKeys.CreateTokenService(options),
            apps.Object,
            tokens.Object,
            provider,
            sessions: sessions.Object);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(SchemataConstants.IdentityClaims.Subject, "user-1"),
            new(AuthorizationConstants.Claims.ClientId, "client-1"),
        ], "grant"));

        var response = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [AuthorizationConstants.Properties.ResponseType]     = AuthorizationConstants.ResponseTypes.Code,
            [AuthorizationConstants.Properties.RedirectUri]      = "https://client.example/callback",
            [AuthorizationConstants.Properties.ResponseMode]     = AuthorizationConstants.ResponseModes.Query,
            [AuthorizationConstants.Properties.Scope]            = AuthorizationConstants.Scopes.OpenId,
            [AuthorizationConstants.Properties.GrantProfile]     = AuthorizationConstants.GrantProfiles.OpenIdConnect,
            [AuthorizationConstants.Properties.SessionStateSalt] = "0011223344556677",
        }, AuthorizationSignInResponseKind.Callback);

        Assert.NotNull(rotated);
        Assert.NotEqual("state-pre-login", rotated);
        var preLogin = new SessionStateFormulator().Build(
            "client-1", "https://client.example", "state-pre-login", "0011223344556677");
        var expected = new SessionStateFormulator().Build(
            "client-1", "https://client.example", rotated!, "0011223344556677");
        Assert.NotNull(response.Callback);
        Assert.Equal(expected, response.Callback.Parameters[AuthorizationConstants.Parameters.SessionState]);
        Assert.NotEqual(preLogin, response.Callback.Parameters[AuthorizationConstants.Parameters.SessionState]);
    }

    [Fact]
    public async Task Omit_Session_State_For_An_Oauth_Callback() {
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = "opstate=state-1";
        using var provider = SessionStateProvider(http);
        var       options  = new SchemataAuthorizationOptions { Issuer = "https://issuer.example" };
        var       tokens   = new Mock<ITokenStore<SchemataToken>>();
        tokens.Setup(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .ReturnsAsync((SchemataToken token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token);
        var apps = new Mock<IApplicationManager<SchemataApplication>>();
        apps.Setup(a => a.FindByClientIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchemataApplication { ClientId = "client-1", CanonicalName = "applications/client-1" });
        var service = new AuthorizationSignInService<SchemataApplication>(
            Options.Create(options),
            Options.Create(new JsonSerializerOptions()),
            TestSecurityKeys.CreateTokenService(options),
            apps.Object,
            tokens.Object,
            provider);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(SchemataConstants.IdentityClaims.Subject, "user-1"),
            new(AuthorizationConstants.Claims.ClientId, "client-1"),
        ], "grant"));

        // An OAuth grant carries the salt property but publishes no session_state.
        var response = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [AuthorizationConstants.Properties.ResponseType]     = AuthorizationConstants.ResponseTypes.Code,
            [AuthorizationConstants.Properties.RedirectUri]      = "https://client.example/callback",
            [AuthorizationConstants.Properties.ResponseMode]     = AuthorizationConstants.ResponseModes.Query,
            [AuthorizationConstants.Properties.Scope]            = "profile",
            [AuthorizationConstants.Properties.GrantProfile]     = AuthorizationConstants.GrantProfiles.OAuth,
            [AuthorizationConstants.Properties.SessionStateSalt] = "0011223344556677",
        }, AuthorizationSignInResponseKind.Callback);

        Assert.NotNull(response.Callback);
        Assert.False(response.Callback.Parameters.ContainsKey(AuthorizationConstants.Parameters.SessionState));
        Assert.False(SessionStateContext.TryGet(http, out _));
        Assert.False(string.IsNullOrWhiteSpace(response.Callback.Parameters[AuthorizationConstants.Parameters.Code]));
    }

    [Fact]
    [Trait("Layer", "Unit")]
    public async Task Aggregate_Logout_Evidence_In_Adapter_Order_And_Reject_Target_Conflicts() {
        var target = new LogoutSessionTarget("users/alice", "sid-a", "applications/client");
        var calls = new List<int>();
        var recent = new Mock<IOpSessionStore>();
        recent.SetupGet(s => s.Order).Returns(20);
        recent.Setup(s => s.ReadLogoutAsync(target, null, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add(20)).ReturnsAsync(new LogoutSessionEvidence(target, false, true));
        var current = new Mock<IOpSessionStore>();
        current.SetupGet(s => s.Order).Returns(10);
        current.Setup(s => s.ReadLogoutAsync(target, null, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add(10)).ReturnsAsync(new LogoutSessionEvidence(target, true, false));
        var service = new DefaultOpSessionService(Options.Create(new SchemataAuthorizationOptions()), [recent.Object, current.Object]);
        Assert.Equal(new LogoutSessionEvidence(target, true, true), await service.ResolveLogoutAsync(target, null));
        Assert.Equal(new[] { 10, 20 }, calls);
        recent.Setup(s => s.ReadLogoutAsync(target, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LogoutSessionEvidence(target with { SessionId = "sid-other" }, false, true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveLogoutAsync(target, null));
    }

    [Fact]
    [Trait("Layer", "Unit")]
    public async Task Propagate_Logout_Evidence_Failure_Without_Persist_Or_Clear() {
        var store = new Mock<IOpSessionStore>(MockBehavior.Strict);
        store.SetupGet(s => s.Order).Returns(0);
        var target = new LogoutSessionTarget("users/alice", "sid-a", "applications/client");
        var failure = new InvalidOperationException("session unavailable");
        store.Setup(s => s.ReadLogoutAsync(target, null, It.IsAny<CancellationToken>())).ThrowsAsync(failure);
        var service = new DefaultOpSessionService(Options.Create(new SchemataAuthorizationOptions()), [store.Object]);
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveLogoutAsync(target, null)));
    }

    private static ServiceProvider SessionStateProvider(DefaultHttpContext http) {
        return new ServiceCollection()
              .AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = http })
              .AddSingleton(new SessionStateFormulator())
              .AddSingleton(Options.Create(new SessionManagementOptions { OpStateCookieName = "opstate" }))
              .BuildServiceProvider();
    }

    private static DefaultOpSessionService OpSessions(DefaultHttpContext http, IOpSessionStore? extra = null) {
        var browser = new OpSessionStore(
            new HttpContextAccessor { HttpContext = http },
            Options.Create(new SessionManagementOptions { OpStateCookieName = "opstate" }));
        return new(
            Options.Create(new SchemataAuthorizationOptions { SessionIdClaimType = "sid" }),
            extra is null ? [browser] : [extra, browser]);
    }

    private static async IAsyncEnumerable<T> Enumerate<T>(params T[] values) {
        foreach (var value in values) {
            yield return value;
        }
        await Task.CompletedTask;
    }
}