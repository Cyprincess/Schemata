using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Schemata.Authorization.Skeleton.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class IntrospectionCnfShould
{
    private const string Issuer = "https://auth.example.com";
    private const string Jkt    = "0ZcOCORZNYy-DWpqq30jZyJGHTN0d2HglBV3uiguA4I";

    private static readonly DateTimeOffset Now = new(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);

    private static Fixture CreateFixture() {
        var opts = Options.Create(new SchemataAuthorizationOptions { Issuer = Issuer });

        var tokensMock   = new Mock<ITokenStore<SchemataToken>>(MockBehavior.Loose);
        var tokenService = TestSecurityKeys.CreateTokenService(opts.Value);

        var app        = new SchemataApplication { Uid = Guid.NewGuid(), ClientId = "test-app" };
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
        services.TryAddEnumerable(ServiceDescriptor
                                   .Scoped<IIntrospectionAdvisor<SchemataApplication>,
                                            AdviceIntrospectionDpop<SchemataApplication>>());
        var sp = services.BuildServiceProvider();

        var handler = new IntrospectionHandler<SchemataApplication>(
            clientAuth.Object, tokenService, tokensMock.Object);
        return new(handler, tokensMock, tokenService, sp);
    }

    private static SchemataToken CreateTokenEntity(string referenceId, string payload, string format) {
        return new() {
            Uid         = Guid.NewGuid(),
            Type        = TokenTypes.AccessToken,
            Application = "test-app",
            ReferenceId = referenceId,
            Payload     = payload,
            Format      = format,
            Status      = TokenStatuses.Valid,
            ExpireTime  = Now.AddHours(1).UtcDateTime,
        };
    }

    private static List<Claim> BuildClaims(bool bound) {
        var claims = new List<Claim> {
            new(Claims.JwtId, Guid.NewGuid().ToString()),
            new(IdentityClaims.Subject, "users/u-42"),
            new(Claims.ClientId, "test-client"),
            new(Claims.Scope, "openid profile"),
            new(Claims.Audience, "api"),
            new(Claims.Issuer, Issuer),
        };

        if (bound) claims.Add(new(Claims.Cnf, $"{{\"jkt\":\"{Jkt}\"}}", JsonClaimValueTypes.Json));

        return claims;
    }

    [Fact]
    public async Task Echo_Cnf_And_Token_Type_Dpop_When_A_Jwt_Bound_Token_Is_Introspected() {
        var f             = CreateFixture();
        using var ambient = AdviceContext.Establish(new(f.Sp));

        var jwt    = await f.TokenService.CreateToken(BuildClaims(true), TimeSpan.FromHours(1));
        var entity = CreateTokenEntity(jwt, jwt, TokenFormats.Jwt);
        f.Tokens.Setup(m => m.FindByReferenceIdAsync(jwt, It.IsAny<CancellationToken>())).ReturnsAsync(entity);

        var response = await f.Handler.HandleAsync(new() { Token = jwt }, null, CancellationToken.None);

        Assert.True(response.Active);
        Assert.Equal(Schemes.Dpop, response.TokenType);
        Assert.NotNull(response.Cnf);
        Assert.Equal(Jkt, response.Cnf![Claims.Jkt]);
    }

    [Fact]
    public async Task Echo_Cnf_And_Token_Type_Dpop_When_A_Reference_Bound_Token_Is_Introspected() {
        var f             = CreateFixture();
        using var ambient = AdviceContext.Establish(new(f.Sp));

        var reference = f.TokenService.CreateReference();
        var payload   = await f.TokenService.CreateToken(BuildClaims(true), TimeSpan.FromHours(1));
        var entity = CreateTokenEntity(reference, payload, TokenFormats.Reference);
        f.Tokens.Setup(m => m.FindByReferenceIdAsync(reference, It.IsAny<CancellationToken>())).ReturnsAsync(entity);

        var response = await f.Handler.HandleAsync(
            new() { Token = reference }, null, CancellationToken.None);

        Assert.True(response.Active);
        Assert.Equal(Schemes.Dpop, response.TokenType);
        Assert.NotNull(response.Cnf);
        Assert.Equal(Jkt, response.Cnf![Claims.Jkt]);
    }

    [Fact]
    public async Task Keep_Token_Type_Bearer_And_Omit_Cnf_When_The_Token_Is_Not_Bound() {
        var f             = CreateFixture();
        using var ambient = AdviceContext.Establish(new(f.Sp));

        var jwt    = await f.TokenService.CreateToken(BuildClaims(false), TimeSpan.FromHours(1));
        var entity = CreateTokenEntity(jwt, jwt, TokenFormats.Jwt);
        f.Tokens.Setup(m => m.FindByReferenceIdAsync(jwt, It.IsAny<CancellationToken>())).ReturnsAsync(entity);

        var response = await f.Handler.HandleAsync(new() { Token = jwt }, null, CancellationToken.None);

        Assert.True(response.Active);
        Assert.Equal(Schemes.Bearer, response.TokenType);
        Assert.Null(response.Cnf);
    }

    [Fact]
    public async Task Degrade_To_Bearer_Without_Echo_When_Cnf_Is_Malformed() {
        var f             = CreateFixture();
        using var ambient = AdviceContext.Establish(new(f.Sp));

        var claims = BuildClaims(false);
        claims.Add(new(Claims.Cnf, "not-json"));
        var jwt    = await f.TokenService.CreateToken(claims, TimeSpan.FromHours(1));
        var entity = CreateTokenEntity(jwt, jwt, TokenFormats.Jwt);
        f.Tokens.Setup(m => m.FindByReferenceIdAsync(jwt, It.IsAny<CancellationToken>())).ReturnsAsync(entity);

        var response = await f.Handler.HandleAsync(new() { Token = jwt }, null, CancellationToken.None);

        Assert.True(response.Active);
        Assert.Equal(Schemes.Bearer, response.TokenType);
        Assert.Null(response.Cnf);
    }

    [Fact]
    public async Task Degrade_To_Bearer_Without_Echo_When_Cnf_Root_Is_Not_A_Json_Object() {
        var f             = CreateFixture();
        using var ambient = AdviceContext.Establish(new(f.Sp));

        var claims = BuildClaims(false);
        claims.Add(new(Claims.Cnf, "[1,2,3]", JsonClaimValueTypes.Json));
        var jwt    = await f.TokenService.CreateToken(claims, TimeSpan.FromHours(1));
        var entity = CreateTokenEntity(jwt, jwt, TokenFormats.Jwt);
        f.Tokens.Setup(m => m.FindByReferenceIdAsync(jwt, It.IsAny<CancellationToken>())).ReturnsAsync(entity);

        var response = await f.Handler.HandleAsync(new() { Token = jwt }, null, CancellationToken.None);

        Assert.True(response.Active);
        Assert.Equal(Schemes.Bearer, response.TokenType);
        Assert.Null(response.Cnf);
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
