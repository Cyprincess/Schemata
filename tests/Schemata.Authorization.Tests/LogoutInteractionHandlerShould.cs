using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class LogoutInteractionHandlerShould
{
    private sealed class Fixture(
        LogoutInteractionHandler<SchemataApplication> handler,
        Mock<ITokenStore<SchemataToken>>              tokens,
        Mock<IOpLogoutService>                        logout,
        SchemataAuthorizationOptions                  options
    ) {
        public LogoutInteractionHandler<SchemataApplication> Handler => handler;
        public Mock<ITokenStore<SchemataToken>>              Tokens  => tokens;
        public Mock<IOpLogoutService>                        Logout  => logout;
        public SchemataAuthorizationOptions                  Options => options;
    }

    private static async Task<Fixture> CreateFixture() {
        var opts = new SchemataAuthorizationOptions {
            Issuer         = "https://localhost",
            InteractionUri = "https://localhost/interact",
        };

        var apps = new Mock<IApplicationManager<SchemataApplication>>();
        apps.Setup(a => a.ValidatePostLogoutRedirectUriAsync(It.IsAny<SchemataApplication?>(), It.IsAny<string?>(),
                                                             It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var tokenService = TestSecurityKeys.CreateTokenService(opts);

        var logout = new Mock<IOpLogoutService>(MockBehavior.Strict);
        logout.Setup(l => l.LogoutAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string?>(), It.IsAny<string?>(),
                                        It.IsAny<CancellationToken>()))
              .ReturnsAsync(new OpLogoutResult([]));

        var tokens = new Mock<ITokenStore<SchemataToken>>(MockBehavior.Strict);
        tokens.Setup(t => t.ListParticipantsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync([]);

        var endSession = new EndSessionHandler<SchemataApplication>(
            apps.Object, tokenService, Options.Create(opts), logout.Object, tokens.Object,
            Options.Create(new JsonSerializerOptions()),
            NullLogger<EndSessionHandler<SchemataApplication>>.Instance);

        var handler = new LogoutInteractionHandler<SchemataApplication>(
            tokens.Object, endSession, Options.Create(new JsonSerializerOptions()), Options.Create(opts));

        return new(handler, tokens, logout, opts);
    }

    private static SchemataToken Confirmation(string subject, string? session, string reference = "ref-1") {
        var payload = JsonSerializer.Serialize(
            new LogoutConfirmationPayload(new() { PostLogoutRedirectUri = "https://example.com/done" }, new(subject, session, string.Empty)),
            new JsonSerializerOptions());
        return new() {
            Type        = TokenTypes.Logout,
            Status      = TokenStatuses.Valid,
            ReferenceId = reference,
            Payload     = payload,
            Parent      = subject,
            SessionId   = session,
            ExpireTime  = DateTime.UtcNow.AddMinutes(5),
        };
    }

    private static ClaimsPrincipal User(string subject, string? session = null) {
        var claims = new List<Claim> { new(IdentityClaims.Subject, subject) };
        if (session is not null) claims.Add(new(Claims.SessionId, session));
        return new(new ClaimsIdentity(claims, "test"));
    }

    private static InteractRequest Request(string reference = "ref-1") => new() { Code = reference, CodeType = TokenTypeUris.Logout };

    [Fact]
    [Trait("Layer", "Component")]
    public async Task Approve_Rejects_A_Mismatched_Confirmation_Subject() {
        var f = await CreateFixture();
        f.Tokens.Setup(t => t.FindByReferenceIdAsync("ref-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(Confirmation("user-1", "sid-1"));

        var error = await Assert.ThrowsAsync<OAuthException>(
            () => f.Handler.ApproveAsync(Request(), User("user-2", "sid-1"), "https://localhost", CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequest, error.Status);
        f.Tokens.Verify(t => t.TryRedeemAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Never);
        f.Logout.Verify(l => l.LogoutAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string?>(), It.IsAny<string?>(),
                                           It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    [Trait("Layer", "Component")]
    public async Task Approve_Rejects_A_Mismatched_Confirmation_Session() {
        var f = await CreateFixture();
        f.Tokens.Setup(t => t.FindByReferenceIdAsync("ref-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(Confirmation("user-1", "sid-1"));

        var error = await Assert.ThrowsAsync<OAuthException>(
            () => f.Handler.ApproveAsync(Request(), User("user-1", "sid-other"), "https://localhost", CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequest, error.Status);
        f.Tokens.Verify(t => t.TryRedeemAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Never);
        f.Logout.Verify(l => l.LogoutAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string?>(), It.IsAny<string?>(),
                                           It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    [Trait("Layer", "Component")]
    public async Task Approve_Rejects_A_Blank_Target_Without_A_Current_Browser_Session() {
        var f = await CreateFixture();
        f.Tokens.Setup(t => t.FindByReferenceIdAsync("ref-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(Confirmation(null!, null));

        var error = await Assert.ThrowsAsync<OAuthException>(
            () => f.Handler.ApproveAsync(Request(), User("user-1"), "https://localhost", CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequest, error.Status);
        f.Tokens.Verify(t => t.TryRedeemAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Never);
        f.Logout.Verify(l => l.LogoutAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string?>(), It.IsAny<string?>(),
                                           It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    [Trait("Layer", "Component")]
    public async Task Approve_Executes_Logout_On_The_Confirmed_Target_Only() {
        var f = await CreateFixture();
        var token = Confirmation("user-1", "sid-1");
        f.Tokens.Setup(t => t.FindByReferenceIdAsync("ref-1", It.IsAny<CancellationToken>())).ReturnsAsync(token);
        f.Tokens.Setup(t => t.TryRedeemAsync(token, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        f.Tokens.Setup(t => t.UpdateAsync(token, It.IsAny<CancellationToken>(),
                                          It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
                .Returns(Task.CompletedTask);
        var result = await f.Handler.ApproveAsync(Request(), User("user-1", "sid-1"), "https://localhost", CancellationToken.None);

        Assert.Equal(AuthorizationStatus.Content, result.Status);
        f.Logout.Verify(l => l.LogoutAsync(It.IsAny<ClaimsPrincipal>(), "user-1", "sid-1",
                                           It.IsAny<CancellationToken>()), Times.Once);
    }
    [Fact]
    [Trait("Layer", "Component")]
    public async Task Approve_Race_Loser_Cannot_Execute_Logout() {
        var f = await CreateFixture();
        var token = Confirmation("user-1", "sid-1");
        f.Tokens.Setup(t => t.FindByReferenceIdAsync("ref-1", It.IsAny<CancellationToken>())).ReturnsAsync(token);
        f.Tokens.Setup(t => t.TryRedeemAsync(token, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var error = await Assert.ThrowsAsync<OAuthException>(() => f.Handler.ApproveAsync(
            Request(), User("user-1", "sid-1"), "https://localhost", CancellationToken.None));
        Assert.Equal(OAuthErrors.InvalidRequest, error.Status);
        f.Logout.Verify(l => l.LogoutAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
