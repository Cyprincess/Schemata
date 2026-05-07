using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Tests;

public class IntrospectionHandlerShould
{
    private const string Issuer = "https://auth.example.com";

    private static readonly RSA            Rsa        = RSA.Create(2048);
    private static readonly RsaSecurityKey SigningKey = new(Rsa);

    private static Fixture CreateFixture(string callerAppName = "test-app") {
        var opts = Options.Create(
            new SchemataAuthorizationOptions {
                Issuer = Issuer, SigningKey = SigningKey, SigningAlgorithm = SigningAlgorithms.RsaSha256,
            }
        );

        var tokensMock   = new Mock<ITokenManager<SchemataToken>>(MockBehavior.Loose);
        var tokenService = new TokenService(opts);

        var app        = new SchemataApplication { Uid = Guid.NewGuid(), ClientId = callerAppName };
        var clientAuth = new Mock<IClientAuthenticationService<SchemataApplication>>();
        clientAuth.Setup(c => c.AuthenticateAsync(
                             It.IsAny<Dictionary<string, List<string?>>?>(),
                             It.IsAny<Dictionary<string, List<string?>>?>(),
                             It.IsAny<Dictionary<string, List<string?>>?>(),
                             It.IsAny<CancellationToken>()
                         )
                   )
                  .ReturnsAsync(app);

        var services = new ServiceCollection();
        services.TryAddEnumerable(
            ServiceDescriptor
               .Scoped<IIntrospectionAdvisor<SchemataApplication, SchemataToken>,
                    AdviceIntrospectionTokenValidation<SchemataApplication, SchemataToken>>()
        );
        var sp = services.BuildServiceProvider();

        var handler = new IntrospectionHandler<SchemataApplication, SchemataToken>(
            clientAuth.Object,
            tokenService,
            tokensMock.Object,
            sp
        );
        return new(handler, tokensMock, tokenService);
    }

    private static SchemataToken CreateTokenEntity(
        string  referenceId,
        string? payload = null,
        string  format  = "jwt",
        string  status  = "valid",
        string? appName = "test-app",
        string  type    = TokenTypes.AccessToken
    ) {
        return new() {
            Uid             = Guid.NewGuid(),
            Type            = type,
            ApplicationName = appName,
            ReferenceId     = referenceId,
            Payload         = payload,
            Format          = format,
            Status          = status,
            ExpireTime      = DateTime.UtcNow.AddHours(1),
        };
    }

    [Fact]
    public async Task ReturnsInactive_WhenTokenNotResolved() {
        var f       = CreateFixture();
        var request = new IntrospectRequest { Token = "invalid-jwt-string" };

        var response = await f.Handler.HandleAsync(request, null, CancellationToken.None);

        Assert.False(response.Active);
    }

    [Fact]
    public async Task ThrowsInvalidRequest_WhenTokenEmpty() {
        var f       = CreateFixture();
        var request = new IntrospectRequest { Token = "" };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => f.Handler.HandleAsync(
                                                              request,
                                                              null,
                                                              CancellationToken.None
                                                          )
        );

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Code);
    }

    [Fact]
    public async Task ThrowsInvalidRequest_WhenTokenWhitespace() {
        var f       = CreateFixture();
        var request = new IntrospectRequest { Token = "   " };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => f.Handler.HandleAsync(
                                                              request,
                                                              null,
                                                              CancellationToken.None
                                                          )
        );

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Code);
    }

    [Fact]
    public async Task ReturnsActive_WhenJwtTokenResolved() {
        var f = CreateFixture();

        var claims = new List<Claim> {
            new(Claims.JwtId, Guid.NewGuid().ToString()),
            new(Claims.Subject, "user-42"),
            new(Claims.ClientId, "test-client"),
            new(Claims.Scope, "openid profile"),
            new(Claims.Audience, "api"),
            new(Claims.Issuer, Issuer),
        };

        var jwt    = f.TokenService.CreateToken(claims, TimeSpan.FromHours(1));
        var entity = CreateTokenEntity(jwt, jwt);

        f.Tokens.Setup(m => m.FindByReferenceIdAsync(jwt, It.IsAny<CancellationToken>())).ReturnsAsync(entity);

        var request  = new IntrospectRequest { Token = jwt };
        var response = await f.Handler.HandleAsync(request, null, CancellationToken.None);

        Assert.True(response.Active);
        Assert.Equal("user-42", response.Sub);
        Assert.Equal("test-client", response.ClientId);
        Assert.Equal("openid profile", response.Scope);
        Assert.Equal(Schemes.Bearer, response.TokenType);
    }

    [Fact]
    public async Task ReturnsActive_WhenCallerDiffersFromTokenClient() {
        // RFC 7662: introspection callers are protected resources, not necessarily
        // the client that issued the token. Access is gated upstream via the
        // ep:introspection permission (AdviceIntrospectionProtectedResource).
        var f = CreateFixture("resource-server");

        var claims = new List<Claim> {
            new(Claims.JwtId, Guid.NewGuid().ToString()),
            new(Claims.Subject, "user-42"),
            new(Claims.ClientId, "other-client"),
            new(Claims.Audience, "api"),
            new(Claims.Issuer, Issuer),
        };

        var jwt    = f.TokenService.CreateToken(claims, TimeSpan.FromHours(1));
        var entity = CreateTokenEntity(jwt, jwt, appName: "other-client");

        f.Tokens.Setup(m => m.FindByReferenceIdAsync(jwt, It.IsAny<CancellationToken>())).ReturnsAsync(entity);

        var response = await f.Handler.HandleAsync(new() { Token = jwt }, null, CancellationToken.None);

        Assert.True(response.Active);
        Assert.Equal("other-client", response.ClientId);
    }

    [Fact]
    public async Task ReturnsInactive_WhenEntityStatusNotValid() {
        var f = CreateFixture();

        var claims = new List<Claim> {
            new(Claims.JwtId, Guid.NewGuid().ToString()), new(Claims.Subject, "user-42"), new(Claims.Audience, "api"),
        };

        var jwt    = f.TokenService.CreateToken(claims, TimeSpan.FromHours(1));
        var entity = CreateTokenEntity(jwt, jwt, "jwt", "revoked");

        f.Tokens.Setup(m => m.FindByReferenceIdAsync(jwt, It.IsAny<CancellationToken>())).ReturnsAsync(entity);

        var request  = new IntrospectRequest { Token = jwt };
        var response = await f.Handler.HandleAsync(request, null, CancellationToken.None);

        Assert.False(response.Active);
    }

    #region Nested type: Fixture

    private record Fixture(
        IntrospectionHandler<SchemataApplication, SchemataToken> Handler,
        Mock<ITokenManager<SchemataToken>>                       Tokens,
        TokenService                                             TokenService
    );

    #endregion
}
