using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
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

public class AuthorizationCodeHandlerShould
{
    private const string TestClientId    = "test-client";
    private const string TestRedirectUri = "https://example.com/callback";
    private const string TestScope       = "openid";
    private const string TestNonce       = "n";
    private const string TestCode        = "auth-code-123";

    private static readonly JsonSerializerOptions JsonOptions = new();

    private static readonly DateTime Anchor = new(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

    private static readonly TimeProvider Clock = new FixedClock(Anchor);

    private static SchemataToken CreateToken(
        string?                 status     = TokenStatuses.Valid,
        DateTime?               expireTime = null,
        string?                 clientId   = TestClientId,
        string?                 redirect   = TestRedirectUri,
        string?                 scope      = TestScope,
        string?                 nonce      = TestNonce,
        string?                 subject    = "user-1",
        AuthenticationContext?  context    = null
    ) {
        var payload = new AuthorizeRequest {
            ClientId    = clientId,
            RedirectUri = redirect,
            Scope       = scope,
            Nonce       = nonce,
        };

        return new() {
            Uid         = Guid.NewGuid(),
            Type        = TokenTypes.AuthorizationCode,
            Status      = status,
            ExpireTime  = expireTime ?? Anchor.AddMinutes(5),
            Parent      = subject,
            Application = $"applications/{clientId}",
            Payload     = JsonSerializer.Serialize(new AuthorizationCodePayload { Request = payload, Context = context }, JsonOptions),
        };
    }

    private static TokenRequest CreateRequest(
        string? code     = TestCode,
        string? clientId = TestClientId,
        string? redirect = TestRedirectUri
    ) {
        return new() {
            GrantType   = GrantTypes.AuthorizationCode,
            Code        = code,
            ClientId    = clientId,
            RedirectUri = redirect,
        };
    }

    private static (AuthorizationCodeHandler<SchemataApplication> Handler, IServiceProvider Sp)
        CreateHandler(
            Mock<ITokenStore<SchemataToken>> tokens
        ) {
        var jsonOpts = Options.Create(JsonOptions);
        var codeOpts = Options.Create(new CodeFlowOptions());
        var app = new SchemataApplication {
            Uid           = Guid.NewGuid(),
            ClientId      = TestClientId,
            CanonicalName = $"applications/{TestClientId}",
        };

        var clientAuth = new Mock<IClientAuthenticationService<SchemataApplication>>();
        clientAuth.Setup(c => c.AuthenticateAsync(It.IsAny<Dictionary<string, List<string?>>?>(),
                                                  It.IsAny<Dictionary<string, List<string?>>?>(),
                                                  It.IsAny<Dictionary<string, List<string?>>?>(),
                                                  It.IsAny<CancellationToken>()))
                  .ReturnsAsync(app);

        var services = new ServiceCollection();
        services.AddSingleton(tokens.Object);
        services.AddSingleton<ICodeExchangeAdvisor<SchemataApplication>>(
            new AdviceCodeExchangeValidation<SchemataApplication>(tokens.Object, Clock));
        var sp = services.BuildServiceProvider();

        return (new(clientAuth.Object, tokens.Object, jsonOpts, codeOpts), sp);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task ThrowsInvalidGrant_WhenCodeEmpty(string? code) {
        var tokens  = new Mock<ITokenStore<SchemataToken>>();
        var (handler, sp) = CreateHandler(tokens);
        using var ambient = AdviceContext.Establish(new(sp));
        var       request = CreateRequest(code);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
                                                              request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Status);
    }

    [Fact]
    public async Task ThrowsInvalidGrant_WhenCodeNotFound() {
        var tokens = new Mock<ITokenStore<SchemataToken>>();
        tokens.Setup(t => t.FindByReferenceIdAsync(TestCode, It.IsAny<CancellationToken>()))
              .ReturnsAsync((SchemataToken?)null);

        var (handler, sp) = CreateHandler(tokens);
        using var ambient = AdviceContext.Establish(new(sp));
        var       request = CreateRequest();

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
                                                              request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Status);
    }

    [Fact]
    public async Task ThrowsInvalidGrant_WhenCodeNotValid() {
        var token  = CreateToken(TokenStatuses.Revoked);
        var tokens = new Mock<ITokenStore<SchemataToken>>();
        tokens.Setup(t => t.FindByReferenceIdAsync(TestCode, It.IsAny<CancellationToken>())).ReturnsAsync(token);

        var (handler, sp) = CreateHandler(tokens);
        using var ambient = AdviceContext.Establish(new(sp));
        var       request = CreateRequest();

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
                                                              request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Status);
    }

    [Fact]
    public async Task ThrowsInvalidGrant_WhenCodeExpired() {
        var token  = CreateToken(expireTime: Anchor.AddMinutes(-1));
        var tokens = new Mock<ITokenStore<SchemataToken>>();
        tokens.Setup(t => t.FindByReferenceIdAsync(TestCode, It.IsAny<CancellationToken>())).ReturnsAsync(token);

        var (handler, sp) = CreateHandler(tokens);
        using var ambient = AdviceContext.Establish(new(sp));
        var       request = CreateRequest();

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
                                                              request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Status);
    }

    [Fact]
    public async Task ThrowsInvalidGrant_WhenClientIdMismatch() {
        var token  = CreateToken(clientId: "other-client");
        var tokens = new Mock<ITokenStore<SchemataToken>>();
        tokens.Setup(t => t.FindByReferenceIdAsync(TestCode, It.IsAny<CancellationToken>())).ReturnsAsync(token);

        var (handler, sp) = CreateHandler(tokens);
        using var ambient = AdviceContext.Establish(new(sp));
        var       request = CreateRequest();

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
                                                              request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Status);
    }

    [Fact]
    public async Task ThrowsInvalidGrant_WhenRedirectUriMismatch() {
        var token  = CreateToken(redirect: "https://other.example.com/callback");
        var tokens = new Mock<ITokenStore<SchemataToken>>();
        tokens.Setup(t => t.FindByReferenceIdAsync(TestCode, It.IsAny<CancellationToken>())).ReturnsAsync(token);

        var (handler, sp) = CreateHandler(tokens);
        using var ambient = AdviceContext.Establish(new(sp));
        var       request = CreateRequest();

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
                                                              request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Status);
    }

    [Fact]
    public async Task MarksCodeRedeemed_OnSuccessfulExchange() {
        var token  = CreateToken();
        var tokens = new Mock<ITokenStore<SchemataToken>>();
        tokens.Setup(t => t.FindByReferenceIdAsync(TestCode, It.IsAny<CancellationToken>())).ReturnsAsync(token);
        tokens.Setup(t => t.TryRedeemAsync(token, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var (handler, sp) = CreateHandler(tokens);
        using var ambient = AdviceContext.Establish(new(sp));
        var       request = CreateRequest();

        await handler.HandleAsync(request, null, CancellationToken.None);

        tokens.Verify(t => t.TryRedeemAsync(token, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ThrowsInvalidGrant_AndCascadesRevocation_WhenRedeemWasLost() {
        var token  = CreateToken();
        token.Authorization = "authorizations/auth-1";
        var tokens = new Mock<ITokenStore<SchemataToken>>();
        tokens.Setup(t => t.FindByReferenceIdAsync(TestCode, It.IsAny<CancellationToken>())).ReturnsAsync(token);
        tokens.Setup(t => t.TryRedeemAsync(token, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var (handler, sp) = CreateHandler(tokens);
        using var ambient = AdviceContext.Establish(new(sp));
        var       request = CreateRequest();

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
                                                              request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Status);
        tokens.Verify(t => t.RevokeByAuthorizationAsync("authorizations/auth-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithValidCodeAndMatchingClient_ReturnsSignInWithGrantTypeScopeNonceAndSubjectClaim() {
        var token  = CreateToken();
        var tokens = new Mock<ITokenStore<SchemataToken>>();
        tokens.Setup(t => t.FindByReferenceIdAsync(TestCode, It.IsAny<CancellationToken>())).ReturnsAsync(token);
        tokens.Setup(t => t.TryRedeemAsync(token, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var (handler, sp) = CreateHandler(tokens);
        using var ambient = AdviceContext.Establish(new(sp));
        var       request = CreateRequest();

        var result = await handler.HandleAsync(request, null, CancellationToken.None);

        Assert.Equal(AuthorizationStatus.SignIn, result.Status);
        Assert.NotNull(result.Principal);
        Assert.NotNull(result.Properties);

        Assert.Equal(GrantTypes.AuthorizationCode, result.Properties[Properties.GrantType]);
        Assert.Equal(TestScope, result.Properties[Properties.Scope]);
        Assert.Equal(TestNonce, result.Properties[Properties.Nonce]);

        var identity = result.Principal.Identity as ClaimsIdentity;
        Assert.NotNull(identity);
        Assert.Equal(SchemataAuthorizationSchemes.Bearer, identity.AuthenticationType);
        Assert.Contains(identity.Claims, c => c is { Type: Claims.ClientId, Value: TestClientId });
        Assert.Contains(identity.Claims, c => c is { Type: IdentityClaims.Subject, Value : "user-1" });
    }

    [Fact]
    public async Task Stamp_The_Context_Persisted_With_The_Code_Onto_The_Exchange_Principal() {
        var token  = CreateToken(context: new(
                       "urn:schemata:acr:classes:multifactor", ["pwd", "otp"], 1767225600));
        var tokens = new Mock<ITokenStore<SchemataToken>>();
        tokens.Setup(t => t.FindByReferenceIdAsync(TestCode, It.IsAny<CancellationToken>())).ReturnsAsync(token);
        tokens.Setup(t => t.TryRedeemAsync(token, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var (handler, sp) = CreateHandler(tokens);
        using var ambient = AdviceContext.Establish(new(sp));

        var result = await handler.HandleAsync(CreateRequest(), null, CancellationToken.None);

        Assert.Equal(AuthorizationStatus.SignIn, result.Status);
        Assert.NotNull(result.Principal);
        var acr      = result.Principal.FindFirst(Claims.Acr);
        var amr      = result.Principal.FindFirst(Claims.Amr);
        var authTime = result.Principal.FindFirst(Claims.AuthTime);
        Assert.NotNull(acr);
        Assert.NotNull(amr);
        Assert.NotNull(authTime);
        Assert.Equal("urn:schemata:acr:classes:multifactor", acr.Value);
        Assert.Equal("""["pwd","otp"]""", amr.Value);
        Assert.Equal("1767225600", authTime.Value);
    }

    [Fact]
    public async Task Stamp_No_Context_Claims_When_The_Code_Carries_None() {
        var token  = CreateToken();
        var tokens = new Mock<ITokenStore<SchemataToken>>();
        tokens.Setup(t => t.FindByReferenceIdAsync(TestCode, It.IsAny<CancellationToken>())).ReturnsAsync(token);
        tokens.Setup(t => t.TryRedeemAsync(token, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var (handler, sp) = CreateHandler(tokens);
        using var ambient = AdviceContext.Establish(new(sp));

        var result = await handler.HandleAsync(CreateRequest(), null, CancellationToken.None);

        Assert.Equal(AuthorizationStatus.SignIn, result.Status);
        Assert.NotNull(result.Principal);
        Assert.DoesNotContain(result.Principal.Claims, c => c.Type is Claims.Acr or Claims.Amr or Claims.AuthTime);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() { return now; }
    }
}
