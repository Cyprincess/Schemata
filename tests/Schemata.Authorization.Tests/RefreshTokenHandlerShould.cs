using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class RefreshTokenHandlerShould
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);

    private static async Task<Fixture> CreateFixture(
        string? approvedScope = "openid profile",
        string? authName      = "auth-1",
        string? sessionId     = "sid-1",
        FakeTimeProvider? time = null
    ) {
        time ??= new(Now);
        var authOpts     = new SchemataAuthorizationOptions { Issuer = "https://auth.example.com" };
        var refreshOpts  = Options.Create(new RefreshTokenFlowOptions());
        var tokenService = TestSecurityKeys.CreateTokenService(authOpts, time: time);

        var claims = new List<Claim> {
            new(IdentityClaims.Subject, "user-1"),
            new(Claims.Scope, approvedScope ?? ""),
            new(Claims.Acr, "urn:example:acr:level-1"),
            new(Claims.Amr, """["pwd","otp"]""", JsonClaimValueTypes.Json),
            new(Claims.AuthTime, "1700000000", ClaimValueTypes.Integer64),
        };
        await using var signing = await tokenService.BeginSigningAsync();
        var jwt = tokenService.CreateToken(
            signing, signing.Signing, claims, time.GetUtcNow(), time.GetUtcNow() + TimeSpan.FromHours(1));

        var refreshToken = new SchemataToken {
            Uid           = Guid.NewGuid(),
            Type          = TokenTypes.RefreshToken,
            Status        = TokenStatuses.Valid,
            ReferenceId   = "rt-ref",
            Payload       = jwt,
            Parent        = "user-1",
            Authorization = authName,
            SessionId     = sessionId,
            Family        = "family-1",
            GrantContext = AuthorizationGrantContexts.Serialize(new() {
                Subject        = "user-1",
                SubjectKind    = GrantSubjectKinds.EndUser,
                Profile        = ScopeParser.Contains(approvedScope, Scopes.OpenId)
                    ? GrantProfiles.OpenIdConnect
                    : GrantProfiles.OAuth,
                Source         = GrantTypes.AuthorizationCode,
                Scope          = approvedScope,
                SessionId      = sessionId,
                Family         = "family-1",
                Authentication = new(
                    "urn:example:acr:level-1", ["pwd", "otp"], 1700000000),
            }),
        };

        var tokens = new Mock<ITokenStore<SchemataToken>>();
        tokens.Setup(t => t.FindByReferenceIdAsync("rt-ref", It.IsAny<CancellationToken>())).ReturnsAsync(refreshToken);
        tokens.Setup(t => t.InvalidateFamilyAsync("family-1", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var app        = new SchemataApplication { Uid = Guid.NewGuid(), ClientId = "test" };
        var clientAuth = new Mock<IClientAuthenticationService<SchemataApplication>>();
        clientAuth.Setup(c => c.AuthenticateAsync(It.IsAny<Dictionary<string, List<string?>>?>(),
                                                  It.IsAny<Dictionary<string, List<string?>>?>(),
                                                  It.IsAny<Dictionary<string, List<string?>>?>(),
                                                  It.IsAny<CancellationToken>(),
                                                  It.IsAny<string?>()))
                  .ReturnsAsync(new ClientAuthenticationResult<SchemataApplication> { Application = app, Method = ClientAuthMethods.ClientSecretPost, Authenticated = true });

        var sp = new ServiceCollection().BuildServiceProvider();
        var handler = new RefreshTokenHandler<SchemataApplication>(
            clientAuth.Object, tokens.Object, tokenService, refreshOpts, sp, time);

        return new(handler, tokens, refreshToken, sp);
    }

    private static TokenRequest CreateRequest(string? scope = null, string? refresh = "rt-ref") {
        return new() {
            GrantType    = GrantTypes.RefreshToken,
            ClientId     = "test",
            RefreshToken = refresh,
            Scope        = scope,
        };
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task ThrowsInvalidGrant_WhenRefreshTokenEmpty(string? refreshToken) {
        var f = await CreateFixture();
        using var ambient = AdviceContext.Establish(new(f.Sp));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => f.Handler.HandleAsync(
                                                              CreateRequest(refresh: refreshToken), null,
                                                              CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Status);
    }

    [Fact]
    public async Task PropagatesAuthorizationNameAndSessionId_OnRotation() {
        var f = await CreateFixture(authName: "auth-42", sessionId: "session-xyz");
        using var ambient = AdviceContext.Establish(new(f.Sp));

        var result = await f.Handler.HandleAsync(CreateRequest(), null, CancellationToken.None);

        Assert.NotNull(result.Properties);
        Assert.Equal("auth-42", result.Properties![Properties.AuthorizationName]);
        Assert.Equal("session-xyz", result.Properties[Properties.SessionId]);
        f.Tokens.Verify(value => value.RevokeByAuthorizationAsync(
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OmitsAuthorizationNameAndSessionId_WhenOriginalTokenHasNone() {
        var f = await CreateFixture(authName: null, sessionId: null);
        using var ambient = AdviceContext.Establish(new(f.Sp));

        var result = await f.Handler.HandleAsync(CreateRequest(), null, CancellationToken.None);

        Assert.NotNull(result.Properties);
        Assert.Null(result.Properties![Properties.AuthorizationName]);
        Assert.Null(result.Properties[Properties.SessionId]);
    }

    [Fact]
    public async Task RevokesTheRefreshFamily_AndFails_WhenThePresentedTokenWasAlreadyRedeemed() {
        var f = await CreateFixture(authName: "auth-42");
        f.RefreshToken.Status = TokenStatuses.Redeemed;
        using var ambient = AdviceContext.Establish(new(f.Sp));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => f.Handler.HandleAsync(CreateRequest(), null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Status);
        f.Tokens.Verify(value => value.InvalidateFamilyAsync("family-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RejectsATokenIssuedToAnotherClient_WithoutTouchingTheGrant() {
        var f = await CreateFixture(authName: "auth-42");
        f.RefreshToken.Application = "applications/other-client";
        using var ambient = AdviceContext.Establish(new(f.Sp));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => f.Handler.HandleAsync(CreateRequest(), null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Status);
        // The wrong client never triggers the replay cascade or consumes the predecessor.
        f.Tokens.Verify(value => value.TryRedeemAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Never);
        f.Tokens.Verify(value => value.RevokeByAuthorizationAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RejectsAnExpiredRefreshRow() {
        var time = new FakeTimeProvider(Now);
        var f = await CreateFixture(time: time);
        f.RefreshToken.ExpireTime = Now.UtcDateTime.AddMinutes(-1);
        using var ambient = AdviceContext.Establish(new(f.Sp));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => f.Handler.HandleAsync(CreateRequest(), null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Status);
        f.Tokens.Verify(value => value.TryRedeemAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CarriesTheOriginalAuthenticationEvents_OntoTheRenewedPrincipal() {
        var f = await CreateFixture();
        var ctx = new AdviceContext(f.Sp);
        using var ambient = AdviceContext.Establish(ctx);

        var result = await f.Handler.HandleAsync(CreateRequest(), null, CancellationToken.None);

        var identity = Assert.IsType<ClaimsIdentity>(result.Principal!.Identity);
        // A renewal re-publishes the original authentication evidence; it is not a new event.
        Assert.NotNull(identity.FindFirst(Claims.Acr));
        Assert.NotNull(identity.FindFirst(Claims.Amr));
        Assert.NotNull(identity.FindFirst(Claims.AuthTime));
        Assert.True(ctx.TryGet<AuthorizationGrantContext>(out var grant));
        Assert.Equal(1700000000, grant!.Authentication?.AuthTime);
        Assert.Equal("urn:example:acr:level-1", grant.Authentication?.Acr);
    }

    [Fact]
    public async Task Downgrade_An_Oidc_Branch_When_Openid_Is_Dropped_Without_Allowing_Reupgrade() {
        var f = await CreateFixture("openid profile");
        using var ambient = AdviceContext.Establish(new(f.Sp));

        var result = await f.Handler.HandleAsync(CreateRequest("profile"), null, CancellationToken.None);

        var grant = AuthorizationGrantContexts.Deserialize(result.Properties![Properties.GrantContext]);
        Assert.Equal(GrantProfiles.OAuth, grant!.Profile);
        f.RefreshToken.GrantContext = result.Properties[Properties.GrantContext];
        Assert.Equal("profile", grant.Scope);
        await Assert.ThrowsAsync<OAuthException>(() => f.Handler.HandleAsync(
            CreateRequest("openid profile"), null, CancellationToken.None));
    }

    #region Nested type: Fixture

    private record Fixture(
        RefreshTokenHandler<SchemataApplication> Handler,
        Mock<ITokenStore<SchemataToken>>                      Tokens,
        SchemataToken                                           RefreshToken,
        IServiceProvider                                        Sp
    );

    #endregion

}
