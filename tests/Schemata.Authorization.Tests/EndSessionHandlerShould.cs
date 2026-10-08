using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Entity.Repository;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

[Trait("Category", "Integration")]
public class EndSessionHandlerShould
{
    private sealed class Fixture
    {
        public readonly FakeTimeProvider Time = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        public readonly Mock<IApplicationManager<SchemataApplication>> Apps = new();
        public readonly Mock<IOpSessionService> Sessions = new();
        public readonly Mock<IOpLogoutService> Logout = new(MockBehavior.Strict);
        public readonly Mock<ITokenStore<SchemataToken>> Tokens = new(MockBehavior.Strict);
        public readonly SchemataAuthorizationOptions Options = new() { Issuer = "https://localhost", InteractionUri = "https://localhost/interact" };
        public SchemataToken? Confirmation;
        public readonly TokenService Issuer;
        public readonly EndSessionHandler<SchemataApplication> Handler;
        public Fixture(IPairwiseSubjectTranslator? pairwise = null) {
            var app = new SchemataApplication { Uid = Guid.NewGuid(), ClientId = "client", CanonicalName = "applications/client" };
            Apps.Setup(a => a.FindByClientIdAsync("client", It.IsAny<CancellationToken>())).ReturnsAsync(app);
            Apps.Setup(a => a.ValidatePostLogoutRedirectUriAsync(app, "https://client.example/done", It.IsAny<CancellationToken>())).ReturnsAsync(true);
            Sessions.Setup(s => s.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ClaimsPrincipal? principal, string? _, CancellationToken _) => principal?.FindFirstValue("sid"));
            Logout.Setup(l => l.LogoutAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(new OpLogoutResult([]));
            Tokens.Setup(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(), It.IsAny<Func<IUnitOfWork, CancellationToken, Task>?>()))
                .Callback<SchemataToken, CancellationToken, Func<IUnitOfWork, CancellationToken, Task>?>((row, _, _) => Confirmation = row)
                .ReturnsAsync((SchemataToken row, CancellationToken _, Func<IUnitOfWork, CancellationToken, Task>? _) => row);
            Issuer = TestSecurityKeys.CreateTokenService(Options, time: Time);
            Handler = new(Apps.Object, Issuer, Microsoft.Extensions.Options.Options.Create(Options), Logout.Object, Tokens.Object,
                Microsoft.Extensions.Options.Options.Create(new JsonSerializerOptions()), NullLogger<EndSessionHandler<SchemataApplication>>.Instance,
                pairwise, Time, Sessions.Object);
        }
        public async Task<EndSessionRequest> RequestAsync(string subject = "users/alice", string sid = "sid-a", bool expired = false) {
            await using var signing = await Issuer.BeginSigningAsync();
            var now = Time.GetUtcNow();
            var hint = Issuer.CreateToken(signing, signing.Signing, [new("sub", subject), new("aud", "client"), new("client_id", "client"), new("sid", sid)], now, now.AddMinutes(1));
            if (expired) Time.Advance(TimeSpan.FromMinutes(2));
            return new() { IdTokenHint = hint, ClientId = "client", PostLogoutRedirectUri = "https://client.example/done", State = "a & b" };
        }
        public void Evidence(bool current, bool recent) {
            Sessions.Setup(s => s.ResolveLogoutAsync(It.IsAny<LogoutSessionTarget>(), It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((LogoutSessionTarget target, ClaimsPrincipal? _, CancellationToken _) => new LogoutSessionEvidence(target, current, recent));
        }
        public LogoutConfirmationPayload Payload() => JsonSerializer.Deserialize<LogoutConfirmationPayload>(Confirmation!.Payload!)!;
        public void NoEffects() => Logout.Verify(l => l.LogoutAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    private static ClaimsPrincipal User(string subject = "users/alice", string sid = "sid-a") => new(new ClaimsIdentity([new("sub", subject), new("sid", sid)], "host"));

    [Theory]
    [Trait("Layer", "Component")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Logout_Current_Proven_Target_And_Return_Registered_State(bool expired) {
        var f = new Fixture();
        f.Evidence(true, false);
        var response = await f.Handler.HandleAsync(await f.RequestAsync(expired: expired), User(), default);
        Assert.Equal("https://client.example/done?state=a%20%26%20b", response.RedirectUri);
        f.Logout.Verify(l => l.LogoutAsync(It.IsAny<ClaimsPrincipal?>(), "users/alice", "sid-a", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Null(f.Confirmation);
    }

    [Theory]
    [Trait("Layer", "Component")]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Confirm_Cookieless_Hint_Without_Requiring_A_Cookie(bool expired, bool recent) {
        var f = new Fixture();
        if (recent) f.Evidence(false, true);
        var response = await f.Handler.HandleAsync(await f.RequestAsync(expired: expired), new(new ClaimsIdentity()), default);
        Assert.StartsWith("https://localhost/interact?", response.RedirectUri);
        Assert.Equal(new LogoutSessionTarget("users/alice", "sid-a", "applications/client"), f.Payload().Target);
        Assert.Equal("https://client.example/done", f.Payload().Request.PostLogoutRedirectUri);
        f.NoEffects();
    }

    [Theory]
    [Trait("Layer", "Component")]
    [InlineData("users/alice", "sid-new", true)]
    [InlineData("users/bob", "sid-b", false)]
    public async Task Bind_Confirmation_To_The_Current_User_And_Session(string subject, string sid, bool retainRp) {
        var f = new Fixture();
        f.Evidence(false, true);
        await f.Handler.HandleAsync(await f.RequestAsync(), User(subject, sid), default);
        Assert.Equal(subject, f.Payload().Target.Subject);
        Assert.Equal(sid, f.Payload().Target.SessionId);
        Assert.Equal(retainRp ? "https://client.example/done" : null, f.Payload().Request.PostLogoutRedirectUri);
        f.NoEffects();
    }

    [Fact]
    [Trait("Layer", "Component")]
    public async Task Confirm_NoHint_Without_Resolving_Rp_Evidence() {
        var f = new Fixture();
        await f.Handler.HandleAsync(new(), User(), default);
        Assert.Equal(new LogoutSessionTarget("users/alice", "sid-a", string.Empty), f.Payload().Target);
        f.Sessions.Verify(s => s.ResolveLogoutAsync(It.IsAny<LogoutSessionTarget>(), It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()), Times.Never);
        f.NoEffects();
    }

    [Fact]
    [Trait("Layer", "Component")]
    public async Task Fail_Without_Interaction_Or_Effects_When_Proof_Is_Absent() {
        var f = new Fixture();
        f.Options.InteractionUri = null;
        var request = await f.RequestAsync(expired: true);
        var error = await Assert.ThrowsAsync<OAuthException>(() => f.Handler.HandleAsync(request, User(), default));
        Assert.Equal(OAuthErrors.ServerError, error.Status);
        Assert.Null(f.Confirmation);
        f.NoEffects();
    }

    [Theory]
    [Trait("Layer", "Component")]
    [InlineData("malformed")]
    [InlineData("signature")]
    [InlineData("issuer")]
    [InlineData("client")]
    public async Task Reject_Invalid_Hint_Before_Any_Authority_Or_Effect(string kind) {
        var f = new Fixture();
        var request = await f.RequestAsync();
        if (kind == "malformed") request.IdTokenHint = "invalid";
        if (kind == "client") request.ClientId = "other";
        if (kind is "signature" or "issuer") {
            var foreign = TestSecurityKeys.CreateTokenService(new() { Issuer = kind == "issuer" ? "https://foreign.example" : "https://localhost" }, time: f.Time);
            await using var signing = await (kind == "issuer" ? f.Issuer : foreign).BeginSigningAsync();
            request.IdTokenHint = foreign.CreateToken(signing, signing.Signing, [new("sub", "users/alice"), new("aud", "client"), new("client_id", "client")], f.Time.GetUtcNow(), f.Time.GetUtcNow().AddMinutes(1));
        }
        var error = await Assert.ThrowsAsync<OAuthException>(() => f.Handler.HandleAsync(request, User(), default));
        Assert.Equal(OAuthErrors.InvalidRequest, error.Status);
        Assert.Null(f.Confirmation);
        f.Sessions.Verify(s => s.ResolveLogoutAsync(It.IsAny<LogoutSessionTarget>(), It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()), Times.Never);
        f.NoEffects();
    }

    [Fact]
    [Trait("Layer", "Component")]
    public async Task Resolve_Pairwise_Subject_Before_Comparing_Or_Executing() {
        var translator = new Mock<IPairwiseSubjectTranslator>();
        translator.Setup(t => t.ToCanonicalAsync("pairwise", It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>())).ReturnsAsync("users/alice");
        var f = new Fixture(translator.Object);
        f.Evidence(true, false);
        await f.Handler.HandleAsync(await f.RequestAsync(subject: "pairwise"), User(), default);
        f.Sessions.Verify(s => s.ResolveLogoutAsync(new("users/alice", "sid-a", "applications/client"), It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()), Times.Once);
        f.Logout.Verify(l => l.LogoutAsync(It.IsAny<ClaimsPrincipal?>(), "users/alice", "sid-a", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Layer", "Component")]
    public async Task Omit_Unregistered_Redirect_And_State_After_Logout() {
        var f = new Fixture();
        f.Evidence(true, false);
        var request = await f.RequestAsync();
        request.PostLogoutRedirectUri = "https://attacker.example";
        var result = await f.Handler.HandleAsync(request, User(), default);
        Assert.Equal(AuthorizationStatus.Content, result.Status);
        Assert.Null(result.RedirectUri);
    }
    [Fact]
    [Trait("Layer", "Component")]
    public async Task Unrecognized_Pairwise_Subject_Cannot_Select_An_Invalidation_Target() {
        var translator = new Mock<IPairwiseSubjectTranslator>();
        translator.Setup(t => t.ToCanonicalAsync(It.IsAny<string?>(), It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        var f = new Fixture(translator.Object);
        await f.Handler.HandleAsync(await f.RequestAsync(subject: "unknown-pairwise"), User(), default);
        Assert.Equal("users/alice", f.Payload().Target.Subject);
        Assert.Equal("sid-a", f.Payload().Target.SessionId);
        Assert.Null(f.Payload().Request.PostLogoutRedirectUri);
        f.NoEffects();
    }

    [Fact]
    [Trait("Layer", "Component")]
    public async Task Render_FrontChannel_Recipients_Before_PostLogout_Redirect() {
        var f = new Fixture();
        f.Evidence(true, false);
        f.Logout.Setup(l => l.LogoutAsync(It.IsAny<ClaimsPrincipal?>(), "users/alice", "sid-a", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpLogoutResult(["https://rp.example/logout"]));
        var result = await f.Handler.HandleAsync(await f.RequestAsync(), User(), default);
        var page = Assert.IsType<LogoutPage>(result.Data);
        Assert.Contains("<iframe src=\"https://rp.example/logout\"", page.Html);
        Assert.Contains("https://client.example/done?state=a%20%26%20b", page.Html);
    }

    [Fact]
    [Trait("Layer", "Unit")]
    public void Render_Only_Notifications_Without_A_Registered_Redirect() {
        var page = EndSessionHandler<SchemataApplication>.BuildLogoutPage(["https://rp.example/logout"], null);
        Assert.Contains("<iframe src=\"https://rp.example/logout\"", page);
        Assert.DoesNotContain("http-equiv=\"refresh\"", page);
        Assert.DoesNotContain("window.location", page);
    }
}
