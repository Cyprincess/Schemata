using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class RevocationHandlerShould
{
    private const string Issuer = "https://auth.example.com";

    private static readonly DateTimeOffset Now = new(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);

    private static Fixture CreateFixture() {
        var opts = Options.Create(new SchemataAuthorizationOptions { Issuer = Issuer });

        var tokensMock   = new Mock<ITokenStore<SchemataToken>>(MockBehavior.Loose);
        var tokenService = TestSecurityKeys.CreateTokenService(opts.Value);

        var app = new SchemataApplication {
            Uid           = Guid.NewGuid(),
            ClientId      = "test-app",
            Name          = "test-app",
            CanonicalName = "applications/test-app",
        };
        var clientAuth = new Mock<IClientAuthenticationService<SchemataApplication>>();
        clientAuth.Setup(c => c.AuthenticateAsync(It.IsAny<Dictionary<string, List<string?>>?>(),
                                                  It.IsAny<Dictionary<string, List<string?>>?>(),
                                                  It.IsAny<Dictionary<string, List<string?>>?>(),
                                                  It.IsAny<CancellationToken>()))
                  .ReturnsAsync(app);

        var services = new ServiceCollection();
        services.TryAddEnumerable(ServiceDescriptor
                                     .Scoped<IRevocationAdvisor<SchemataApplication>,
                                          AdviceRevocationTokenValidation<SchemataApplication>>());
        var sp = services.BuildServiceProvider();

        var handler = new RevocationHandler<SchemataApplication>(
            clientAuth.Object, tokensMock.Object);
        return new(handler, tokensMock, tokenService, sp);
    }

    private static SchemataToken CreateTokenEntity(
        string  referenceId,
        string  format  = "jwt",
        string  status  = "valid",
        string? appName = "applications/test-app",
        string? payload = null,
        string  type    = TokenTypes.AccessToken
    ) {
        return new() {
            Uid         = Guid.NewGuid(),
            Type        = type,
            Application = appName,
            ReferenceId = referenceId,
            Format      = format,
            Status      = status,
            Payload     = payload,
            ExpireTime  = Now.AddHours(1).UtcDateTime,
        };
    }

    [Fact]
    public async Task ThrowsInvalidRequest_WhenTokenEmpty() {
        var       f       = CreateFixture();
        using var ambient = AdviceContext.Establish(new(f.Sp));
        var       request = new RevokeRequest { Token = "" };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => f.Handler.HandleAsync(
                                                              request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
    }

    [Fact]
    public async Task ThrowsInvalidRequest_WhenTokenWhitespace() {
        var       f       = CreateFixture();
        using var ambient = AdviceContext.Establish(new(f.Sp));
        var       request = new RevokeRequest { Token = "   " };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => f.Handler.HandleAsync(
                                                              request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
    }

    [Fact]
    public async Task DoesNotThrow_WhenTokenNotFound() {
        var f = CreateFixture();
        using var ambient = AdviceContext.Establish(new(f.Sp));

        f.Tokens.Setup(m => m.FindByReferenceIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
         .ReturnsAsync((SchemataToken?)null);

        var request = new RevokeRequest { Token = "unknown-token" };

        await f.Handler.HandleAsync(request, null, CancellationToken.None);

        f.Tokens.Verify(m => m.RevokeAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RevokesToken_WhenJwtTokenResolved() {
        var f = CreateFixture();
        using var ambient = AdviceContext.Establish(new(f.Sp));

        var claims = new List<Claim> {
            new(Claims.JwtId, Guid.NewGuid().ToString()),
            new(IdentityClaims.Subject, "user-42"),
            new(Claims.Audience, "api"),
        };

        var jwt    = await f.TokenService.CreateToken(claims, TimeSpan.FromHours(1));
        var entity = CreateTokenEntity(jwt);

        f.Tokens.Setup(m => m.FindByReferenceIdAsync(jwt, It.IsAny<CancellationToken>())).ReturnsAsync(entity);

        var request = new RevokeRequest { Token = jwt };

        await f.Handler.HandleAsync(request, null, CancellationToken.None);

        f.Tokens.Verify(m => m.RevokeAsync(entity, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FallsBackToReferenceIdLookup_WhenResolverReturnsNull() {
        var       f       = CreateFixture();
        using var ambient = AdviceContext.Establish(new(f.Sp));
        var       refId   = "opaque-ref-123";
        var       entity  = CreateTokenEntity(refId, "reference");

        f.Tokens.Setup(m => m.FindByReferenceIdAsync(refId, It.IsAny<CancellationToken>())).ReturnsAsync(entity);

        var request = new RevokeRequest { Token = refId };

        await f.Handler.HandleAsync(request, null, CancellationToken.None);

        f.Tokens.Verify(m => m.RevokeAsync(entity, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DoesNotRevoke_WhenEntityStatusRevoked() {
        var f = CreateFixture();
        using var ambient = AdviceContext.Establish(new(f.Sp));

        var claims = new List<Claim> {
            new(Claims.JwtId, Guid.NewGuid().ToString()),
            new(IdentityClaims.Subject, "user-42"),
            new(Claims.Audience, "api"),
        };

        var jwt    = await f.TokenService.CreateToken(claims, TimeSpan.FromHours(1));
        var entity = CreateTokenEntity(jwt, "jwt", "revoked");

        f.Tokens.Setup(m => m.FindByReferenceIdAsync(jwt, It.IsAny<CancellationToken>())).ReturnsAsync(entity);

        var request = new RevokeRequest { Token = jwt };

        await f.Handler.HandleAsync(request, null, CancellationToken.None);

        f.Tokens.Verify(m => m.RevokeAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #region Nested type: Fixture

    private record Fixture(
        RevocationHandler<SchemataApplication> Handler,
        Mock<ITokenStore<SchemataToken>>                    Tokens,
        TokenService                                          TokenService,
        IServiceProvider                                      Sp
    );

    #endregion
}
