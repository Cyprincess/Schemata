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

public class IntrospectionHandlerShould
{
    private const string Issuer = "https://auth.example.com";

    private static readonly DateTimeOffset Now = new(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);

    private static Fixture CreateFixture(string callerAppName = "test-app") {
        var opts = Options.Create(new SchemataAuthorizationOptions { Issuer = Issuer });

        var tokensMock   = new Mock<ITokenStore<SchemataToken>>(MockBehavior.Loose);
        var tokenService = TestSecurityKeys.CreateTokenService(opts.Value);

        var app        = new SchemataApplication { Uid = Guid.NewGuid(), ClientId = callerAppName };
        var clientAuth = new Mock<IClientAuthenticationService<SchemataApplication>>();
        clientAuth.Setup(c => c.AuthenticateAsync(It.IsAny<Dictionary<string, List<string?>>?>(),
                                                  It.IsAny<Dictionary<string, List<string?>>?>(),
                                                  It.IsAny<Dictionary<string, List<string?>>?>(),
                                                  It.IsAny<CancellationToken>()))
                  .ReturnsAsync(app);

        var services = new ServiceCollection();
        services.TryAddEnumerable(ServiceDescriptor
                                     .Scoped<IIntrospectionAdvisor<SchemataApplication>,
                                          AdviceIntrospectionTokenValidation<SchemataApplication>>());
        var sp = services.BuildServiceProvider();

        var handler = new IntrospectionHandler<SchemataApplication>(
            clientAuth.Object, tokenService, tokensMock.Object);
        return new(handler, tokensMock, tokenService, sp);
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
            Uid         = Guid.NewGuid(),
            Type        = type,
            Application = appName,
            ReferenceId = referenceId,
            Payload     = payload,
            Format      = format,
            Status      = status,
            ExpireTime  = Now.AddHours(1).UtcDateTime,
        };
    }

    [Fact]
    public async Task ReturnsInactive_WhenTokenNotResolved() {
        var       f       = CreateFixture();
        using var ambient = AdviceContext.Establish(new(f.Sp));
        var       request = new IntrospectRequest { Token = "invalid-jwt-string" };

        var response = await f.Handler.HandleAsync(request, null, CancellationToken.None);

        Assert.False(response.Active);
    }

    [Fact]
    public async Task ThrowsInvalidRequest_WhenTokenEmpty() {
        var       f       = CreateFixture();
        using var ambient = AdviceContext.Establish(new(f.Sp));
        var       request = new IntrospectRequest { Token = "" };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => f.Handler.HandleAsync(
                                                              request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
    }

    [Fact]
    public async Task ThrowsInvalidRequest_WhenTokenWhitespace() {
        var       f       = CreateFixture();
        using var ambient = AdviceContext.Establish(new(f.Sp));
        var       request = new IntrospectRequest { Token = "   " };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => f.Handler.HandleAsync(
                                                              request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
    }

    [Fact]
    public async Task ReturnsActive_WhenJwtTokenResolved() {
        var f = CreateFixture();
        using var ambient = AdviceContext.Establish(new(f.Sp));

        var claims = new List<Claim> {
            new(Claims.JwtId, Guid.NewGuid().ToString()),
            new(IdentityClaims.Subject, "users/u-42"),
            new(Claims.ClientId, "test-client"),
            new(Claims.Scope, "openid profile"),
            new(Claims.Audience, "api"),
            new(Claims.Issuer, Issuer),
        };

        var jwt    = await f.TokenService.CreateToken(claims, TimeSpan.FromHours(1));
        var entity = CreateTokenEntity(jwt, jwt);

        f.Tokens.Setup(m => m.FindByReferenceIdAsync(jwt, It.IsAny<CancellationToken>())).ReturnsAsync(entity);

        var request  = new IntrospectRequest { Token = jwt };
        var response = await f.Handler.HandleAsync(request, null, CancellationToken.None);

        Assert.True(response.Active);
        Assert.Equal("users/u-42", response.Sub);
        Assert.Equal("test-client", response.ClientId);
        Assert.Equal("openid profile", response.Scope);
        Assert.Equal(Schemes.Bearer, response.TokenType);
    }

    [Fact]
    public async Task Echoes_Acr_And_AuthTime_From_The_Token() {
        var f = CreateFixture();
        using var ambient = AdviceContext.Establish(new(f.Sp));

        var claims = new List<Claim> {
            new(Claims.JwtId, Guid.NewGuid().ToString()),
            new(IdentityClaims.Subject, "users/u-42"),
            new(Claims.ClientId, "test-client"),
            new(Claims.Audience, "api"),
            new(Claims.Issuer, Issuer),
            new(Claims.Acr, "urn:schemata:acr:classes:multifactor"),
            new(Claims.Amr, """["pwd","otp"]"""),
            new(Claims.AuthTime, "1767225600"),
        };

        var jwt    = await f.TokenService.CreateToken(claims, TimeSpan.FromHours(1));
        var entity = CreateTokenEntity(jwt, jwt);

        f.Tokens.Setup(m => m.FindByReferenceIdAsync(jwt, It.IsAny<CancellationToken>())).ReturnsAsync(entity);

        var request  = new IntrospectRequest { Token = jwt };
        var response = await f.Handler.HandleAsync(request, null, CancellationToken.None);

        Assert.True(response.Active);
        Assert.Equal("urn:schemata:acr:classes:multifactor", response.Acr);
        Assert.Equal(1767225600, response.AuthTime);
    }

    [Fact]
    public async Task Echoes_Nothing_When_The_Token_Carries_No_Context_Claims() {
        var f = CreateFixture();
        using var ambient = AdviceContext.Establish(new(f.Sp));

        var claims = new List<Claim> {
            new(Claims.JwtId, Guid.NewGuid().ToString()),
            new(IdentityClaims.Subject, "users/u-42"),
            new(Claims.ClientId, "test-client"),
            new(Claims.Audience, "api"),
            new(Claims.Issuer, Issuer),
        };

        var jwt    = await f.TokenService.CreateToken(claims, TimeSpan.FromHours(1));
        var entity = CreateTokenEntity(jwt, jwt);

        f.Tokens.Setup(m => m.FindByReferenceIdAsync(jwt, It.IsAny<CancellationToken>())).ReturnsAsync(entity);

        var request  = new IntrospectRequest { Token = jwt };
        var response = await f.Handler.HandleAsync(request, null, CancellationToken.None);

        Assert.True(response.Active);
        Assert.Null(response.Acr);
        Assert.Null(response.AuthTime);
    }

    [Fact]
    public async Task ReturnsActive_WhenCallerDiffersFromTokenClient() {
        // RFC 7662 introspection callers are protected resources, with access gated
        // upstream via the ep:introspection permission.
        var f = CreateFixture("resource-server");
        using var ambient = AdviceContext.Establish(new(f.Sp));

        var claims = new List<Claim> {
            new(Claims.JwtId, Guid.NewGuid().ToString()),
            new(IdentityClaims.Subject, "user-42"),
            new(Claims.ClientId, "other-client"),
            new(Claims.Audience, "api"),
            new(Claims.Issuer, Issuer),
        };

        var jwt    = await f.TokenService.CreateToken(claims, TimeSpan.FromHours(1));
        var entity = CreateTokenEntity(jwt, jwt, appName: "other-client");

        f.Tokens.Setup(m => m.FindByReferenceIdAsync(jwt, It.IsAny<CancellationToken>())).ReturnsAsync(entity);

        var response = await f.Handler.HandleAsync(new() { Token = jwt }, null, CancellationToken.None);

        Assert.True(response.Active);
        Assert.Equal("other-client", response.ClientId);
    }

    [Fact]
    public async Task ReturnsInactive_WhenEntityStatusNotValid() {
        var f = CreateFixture();
        using var ambient = AdviceContext.Establish(new(f.Sp));

        var claims = new List<Claim> {
            new(Claims.JwtId, Guid.NewGuid().ToString()),
            new(IdentityClaims.Subject, "user-42"),
            new(Claims.Audience, "api"),
        };

        var jwt    = await f.TokenService.CreateToken(claims, TimeSpan.FromHours(1));
        var entity = CreateTokenEntity(jwt, jwt, "jwt", "revoked");

        f.Tokens.Setup(m => m.FindByReferenceIdAsync(jwt, It.IsAny<CancellationToken>())).ReturnsAsync(entity);

        var request  = new IntrospectRequest { Token = jwt };
        var response = await f.Handler.HandleAsync(request, null, CancellationToken.None);

        Assert.False(response.Active);
    }

    #region Nested type: Fixture

    private record Fixture(
        IntrospectionHandler<SchemataApplication> Handler,
        Mock<ITokenStore<SchemataToken>>                       Tokens,
        TokenService                                             TokenService,
        IServiceProvider                                         Sp
    );

    #endregion
}
