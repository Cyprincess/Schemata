using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Tests;

public class RefreshTokenHandlerShould
{
    private static Fixture CreateFixture(
        string? approvedScope = "openid profile",
        string? authName      = "auth-1",
        string? sessionId     = "sid-1"
    ) {
        var authOpts = new SchemataAuthorizationOptions { Issuer = "https://auth.example.com" };
        authOpts.AddEphemeralSigningKey();
        var opts         = Options.Create(authOpts);
        var refreshOpts  = Options.Create(new RefreshTokenFlowOptions());
        var tokenService = new TokenService(opts);

        var claims = new List<Claim> {
            new(Claims.Subject, "user-1"),
            new(Claims.Scope,   approvedScope ?? ""),
        };
        var jwt = tokenService.CreateToken(claims, TimeSpan.FromHours(1));

        var refreshToken = new SchemataToken {
            Id                = 1,
            Type              = TokenTypes.RefreshToken,
            Status            = TokenStatuses.Valid,
            ReferenceId       = "rt-ref",
            Payload           = jwt,
            Subject           = "user-1",
            AuthorizationName = authName,
            SessionId         = sessionId,
        };

        var tokens = new Mock<ITokenManager<SchemataToken>>();
        tokens.Setup(t => t.FindByReferenceIdAsync("rt-ref", It.IsAny<CancellationToken>()))
              .ReturnsAsync(refreshToken);

        var app = new SchemataApplication { Id = 1, ClientId = "test" };
        var clientAuth = new Mock<IClientAuthenticationService<SchemataApplication>>();
        clientAuth.Setup(c => c.AuthenticateAsync(
                              It.IsAny<Dictionary<string, List<string?>>?>(), It.IsAny<Dictionary<string, List<string?>>?>(),
                              It.IsAny<Dictionary<string, List<string?>>?>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(app);

        var sp      = new ServiceCollection().BuildServiceProvider();
        var handler = new RefreshTokenHandler<SchemataApplication, SchemataToken>(clientAuth.Object, tokens.Object, tokenService, refreshOpts, sp);

        return new(handler, tokens, refreshToken);
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
        var f = CreateFixture();

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => f.Handler.HandleAsync(CreateRequest(refresh: refreshToken), null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Code);
    }

    [Fact]
    public async Task PropagatesAuthorizationNameAndSessionId_OnRotation() {
        var f = CreateFixture(authName: "auth-42", sessionId: "session-xyz");

        var result = await f.Handler.HandleAsync(CreateRequest(), null, CancellationToken.None);

        Assert.NotNull(result.Properties);
        Assert.Equal("auth-42",
                     result.Properties![Properties.AuthorizationName]);
        Assert.Equal("session-xyz",
                     result.Properties[Properties.SessionId]);
    }

    [Fact]
    public async Task OmitsAuthorizationNameAndSessionId_WhenOriginalTokenHasNone() {
        var f = CreateFixture(authName: null, sessionId: null);

        var result = await f.Handler.HandleAsync(CreateRequest(), null, CancellationToken.None);

        Assert.NotNull(result.Properties);
        Assert.Null(result.Properties![Properties.AuthorizationName]);
        Assert.Null(result.Properties[Properties.SessionId]);
    }

    #region Nested type: Fixture

    private record Fixture(
        RefreshTokenHandler<SchemataApplication, SchemataToken> Handler,
        Mock<ITokenManager<SchemataToken>> Tokens,
        SchemataToken                      RefreshToken
    );

    #endregion
}
