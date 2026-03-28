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

public class RevocationHandlerShould
{
    private const string Issuer = "https://auth.example.com";

    private static readonly RSA            Rsa        = RSA.Create(2048);
    private static readonly RsaSecurityKey SigningKey = new(Rsa);

    private static Fixture CreateFixture() {
        var opts = Options.Create(new SchemataAuthorizationOptions {
            Issuer           = Issuer,
            SigningKey       = SigningKey,
            SigningAlgorithm = SigningAlgorithms.RsaSha256,
        });

        var tokensMock   = new Mock<ITokenManager<SchemataToken>>(MockBehavior.Loose);
        var tokenService = new TokenService(opts);

        var app = new SchemataApplication { Id = 1, Name = "test-app", ClientId = "test-app" };
        var clientAuth = new Mock<IClientAuthenticationService<SchemataApplication>>();
        clientAuth.Setup(c => c.AuthenticateAsync(It.IsAny<Dictionary<string, List<string?>>?>(), It.IsAny<Dictionary<string, List<string?>>?>(), It.IsAny<Dictionary<string, List<string?>>?>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(app);

        var services = new ServiceCollection();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRevocationAdvisor<SchemataApplication, SchemataToken>, AdviceRevocationTokenValidation<SchemataApplication, SchemataToken>>());
        var sp = services.BuildServiceProvider();

        var handler = new RevocationHandler<SchemataApplication, SchemataToken>(clientAuth.Object, tokensMock.Object, sp);
        return new(handler, tokensMock, tokenService);
    }

    private static SchemataToken CreateTokenEntity(
        string  referenceId,
        string  format  = "jwt",
        string  status  = "valid",
        string? appName = "test-app",
        string? payload = null,
        string  type    = TokenTypes.AccessToken
    ) {
        return new() {
            Id              = 1,
            Type            = type,
            ApplicationName = appName,
            ReferenceId     = referenceId,
            Format          = format,
            Status          = status,
            Payload         = payload,
            ExpireTime      = DateTime.UtcNow.AddHours(1),
        };
    }

    [Fact]
    public async Task ThrowsInvalidRequest_WhenTokenEmpty() {
        var f       = CreateFixture();
        var request = new RevokeRequest { Token = "" };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => f.Handler.HandleAsync(request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Code);
    }

    [Fact]
    public async Task ThrowsInvalidRequest_WhenTokenWhitespace() {
        var f       = CreateFixture();
        var request = new RevokeRequest { Token = "   " };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => f.Handler.HandleAsync(request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Code);
    }

    [Fact]
    public async Task DoesNotThrow_WhenTokenNotFound() {
        var f = CreateFixture();

        f.Tokens.Setup(m => m.FindByReferenceIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
         .ReturnsAsync((SchemataToken?)null);

        var request = new RevokeRequest { Token = "unknown-token" };

        await f.Handler.HandleAsync(request, null, CancellationToken.None);

        f.Tokens.Verify(m => m.RevokeAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RevokesToken_WhenJwtTokenResolved() {
        var f = CreateFixture();

        var claims = new List<Claim> {
            new(Claims.JwtId, Guid.NewGuid().ToString()),
            new(Claims.Subject, "user-42"),
            new(Claims.Audience, "api"),
        };

        var jwt    = f.TokenService.CreateToken(claims, TimeSpan.FromHours(1));
        var entity = CreateTokenEntity(jwt);

        f.Tokens.Setup(m => m.FindByReferenceIdAsync(jwt, It.IsAny<CancellationToken>())).ReturnsAsync(entity);

        var request = new RevokeRequest { Token = jwt };

        await f.Handler.HandleAsync(request, null, CancellationToken.None);

        f.Tokens.Verify(m => m.RevokeAsync(entity, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FallsBackToReferenceIdLookup_WhenResolverReturnsNull() {
        var f      = CreateFixture();
        var refId  = "opaque-ref-123";
        var entity = CreateTokenEntity(refId, "reference");

        f.Tokens.Setup(m => m.FindByReferenceIdAsync(refId, It.IsAny<CancellationToken>())).ReturnsAsync(entity);

        var request = new RevokeRequest { Token = refId };

        await f.Handler.HandleAsync(request, null, CancellationToken.None);

        f.Tokens.Verify(m => m.RevokeAsync(entity, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DoesNotRevoke_WhenEntityStatusRevoked() {
        var f = CreateFixture();

        var claims = new List<Claim> {
            new(Claims.JwtId, Guid.NewGuid().ToString()),
            new(Claims.Subject, "user-42"),
            new(Claims.Audience, "api"),
        };

        var jwt    = f.TokenService.CreateToken(claims, TimeSpan.FromHours(1));
        var entity = CreateTokenEntity(jwt, "jwt", "revoked");

        f.Tokens.Setup(m => m.FindByReferenceIdAsync(jwt, It.IsAny<CancellationToken>())).ReturnsAsync(entity);

        var request = new RevokeRequest { Token = jwt };

        await f.Handler.HandleAsync(request, null, CancellationToken.None);

        f.Tokens.Verify(m => m.RevokeAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #region Nested type: Fixture

    private record Fixture(
        RevocationHandler<SchemataApplication, SchemataToken> Handler,
        Mock<ITokenManager<SchemataToken>> Tokens,
        TokenService                       TokenService
    );

    #endregion
}
