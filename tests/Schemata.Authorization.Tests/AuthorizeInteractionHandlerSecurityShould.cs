using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class AuthorizeInteractionHandlerSecurityShould
{
    private static readonly DateTime Anchor = new(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

    private const string Issuer = "https://auth.example.com";

    private const string InteractionCode = "interact-ref";

    private const string AccessTokenReference = "access-ref";

    [Fact]
    public async Task Deny_Revokes_Only_The_Interaction_Token_And_Leaves_The_Access_Token_Reference_Valid() {
        var (handler, tokens, _, interaction, accessToken) = CreateFixture();

        await handler.DenyAsync(new() { Code = InteractionCode }, CancellationToken.None);

        Assert.Equal(TokenStatuses.Revoked, interaction.Status);
        Assert.Equal(TokenStatuses.Valid, accessToken.Status);
        tokens.Verify(t => t.RevokeAsync(interaction, It.IsAny<CancellationToken>()), Times.Once);
        tokens.Verify(t => t.RevokeAsync(accessToken, It.IsAny<CancellationToken>()), Times.Never);
        tokens.Verify(t => t.FindByReferenceIdAsync(AccessTokenReference, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Approve_Second_Attempt_With_The_Same_Interaction_Token_Fails_As_Invalid_Grant() {
        var (handler, tokens, authzMgr, _, _) = CreateFixture();
        var principal = CreatePrincipal();

        await handler.ApproveAsync(new() { Code = InteractionCode }, principal, Issuer, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.ApproveAsync(
                                                             new() { Code = InteractionCode }, principal, Issuer,
                                                             CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Status);
        tokens.Verify(t => t.RevokeAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Once);
        authzMgr.Verify(m => m.CreateAsync(It.IsAny<SchemataAuthorization>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // The mocked store mirrors the row state machine the handler depends on: a revoked row keeps
    // its reference resolvable, but it no longer carries the Valid status the flow requires.
    private static (AuthorizeInteractionHandler<SchemataApplication, SchemataAuthorization, SchemataScope> Handler,
        Mock<ITokenStore<SchemataToken>>                                                      Tokens,
        Mock<IAuthorizationManager<SchemataAuthorization>>                                    AuthzMgr,
        SchemataToken                                                                         Interaction,
        SchemataToken                                                                         AccessToken) CreateFixture() {
        var jsonOpts = Options.Create(new JsonSerializerOptions());
        var authOpts = Options.Create(new SchemataAuthorizationOptions { SessionIdClaimType = "sid" });

        var app = new SchemataApplication {
            Uid           = Guid.NewGuid(),
            ClientId      = "browser-client",
            Name          = "browser-client",
            CanonicalName = "applications/browser-client",
        };
        var apps = new Mock<IApplicationManager<SchemataApplication>>();
        apps.Setup(a => a.FindByClientIdAsync("browser-client", It.IsAny<CancellationToken>())).ReturnsAsync(app);

        var payload = JsonSerializer.Serialize(new AuthorizeRequest {
            ClientId     = "browser-client",
            RedirectUri  = "https://localhost/callback",
            ResponseType = ResponseTypes.Code,
            Scope        = "openid",
        }, jsonOpts.Value);

        var interaction = new SchemataToken {
            Uid         = Guid.NewGuid(),
            Name        = "interact-security",
            Type        = TokenTypes.Interaction,
            Status      = TokenStatuses.Valid,
            ReferenceId = InteractionCode,
            Payload     = payload,
            ExpireTime  = Anchor.AddMinutes(10),
        };
        var accessToken = new SchemataToken {
            Uid         = Guid.NewGuid(),
            Name        = "access-security",
            Type        = TokenTypes.AccessToken,
            Status      = TokenStatuses.Valid,
            ReferenceId = AccessTokenReference,
            ExpireTime  = Anchor.AddHours(1),
        };

        var tokens = new Mock<ITokenStore<SchemataToken>>();
        tokens.Setup(t => t.FindByReferenceIdAsync(InteractionCode, It.IsAny<CancellationToken>()))
              .ReturnsAsync(interaction);
        tokens.Setup(t => t.RevokeAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
              .Callback((SchemataToken token, CancellationToken _) => token.Status = TokenStatuses.Revoked)
              .Returns(Task.CompletedTask);

        var authzMgr = new Mock<IAuthorizationManager<SchemataAuthorization>>();
        authzMgr.Setup(m => m.CreateAsync(It.IsAny<SchemataAuthorization>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((SchemataAuthorization a, CancellationToken _) => a);

        var handler = new AuthorizeInteractionHandler<SchemataApplication, SchemataAuthorization, SchemataScope>(
            apps.Object, authzMgr.Object, new Mock<IScopeManager<SchemataScope>>().Object, tokens.Object,
            jsonOpts, authOpts, null, new FixedClock(Anchor));

        return (handler, tokens, authzMgr, interaction, accessToken);
    }

    private static ClaimsPrincipal CreatePrincipal() {
        return new(new ClaimsIdentity([new(IdentityClaims.Subject, "users/u-42")], "test"));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() { return now; }
    }
}
