using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Tests;

public class AuthorizationCodeHandlerShould
{
    private const string TestClientId    = "test-client";
    private const string TestRedirectUri = "https://example.com/callback";
    private const string TestScope       = "openid";
    private const string TestNonce       = "n";
    private const string TestCode        = "auth-code-123";

    private static readonly JsonSerializerOptions JsonOptions = new();

    private static SchemataToken CreateToken(
        string?   status     = TokenStatuses.Valid,
        DateTime? expireTime = null,
        string?   clientId   = TestClientId,
        string?   redirect   = TestRedirectUri,
        string?   scope      = TestScope,
        string?   nonce      = TestNonce,
        string?   subject    = "user-1"
    ) {
        var payload = new AuthorizeRequest {
            ClientId    = clientId,
            RedirectUri = redirect,
            Scope       = scope,
            Nonce       = nonce,
        };

        return new() {
            Id              = 1,
            Type            = TokenTypes.AuthorizationCode,
            Status          = status,
            ExpireTime      = expireTime ?? DateTime.UtcNow.AddMinutes(5),
            Subject         = subject,
            ApplicationName = clientId,
            Payload         = JsonSerializer.Serialize(payload, JsonOptions),
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

    private static AuthorizationCodeHandler<SchemataApplication, SchemataToken> CreateHandler(
        Mock<ITokenManager<SchemataToken>> tokens
    ) {
        var jsonOpts = Options.Create(JsonOptions);
        var codeOpts = Options.Create(new CodeFlowOptions());
        var app      = new SchemataApplication { Id = 1, ClientId = TestClientId };

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
        services.AddSingleton(tokens.Object);
        services.AddSingleton<ICodeExchangeAdvisor<SchemataApplication, SchemataToken>>(
            new AdviceCodeExchangeValidation<SchemataApplication, SchemataToken>()
        );
        var sp = services.BuildServiceProvider();

        return new(clientAuth.Object, tokens.Object, sp, jsonOpts, codeOpts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task ThrowsInvalidGrant_WhenCodeEmpty(string? code) {
        var tokens  = new Mock<ITokenManager<SchemataToken>>();
        var handler = CreateHandler(tokens);
        var request = CreateRequest(code);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
                                                              request,
                                                              null,
                                                              CancellationToken.None
                                                          )
        );

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Code);
    }

    [Fact]
    public async Task ThrowsInvalidGrant_WhenCodeNotFound() {
        var tokens = new Mock<ITokenManager<SchemataToken>>();
        tokens.Setup(t => t.FindByReferenceIdAsync(TestCode, It.IsAny<CancellationToken>()))
              .ReturnsAsync((SchemataToken?)null);

        var handler = CreateHandler(tokens);
        var request = CreateRequest();

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
                                                              request,
                                                              null,
                                                              CancellationToken.None
                                                          )
        );

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Code);
    }

    [Fact]
    public async Task ThrowsInvalidGrant_WhenCodeNotValid() {
        var token  = CreateToken(TokenStatuses.Revoked);
        var tokens = new Mock<ITokenManager<SchemataToken>>();
        tokens.Setup(t => t.FindByReferenceIdAsync(TestCode, It.IsAny<CancellationToken>())).ReturnsAsync(token);

        var handler = CreateHandler(tokens);
        var request = CreateRequest();

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
                                                              request,
                                                              null,
                                                              CancellationToken.None
                                                          )
        );

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Code);
    }

    [Fact]
    public async Task ThrowsInvalidGrant_WhenCodeExpired() {
        var token  = CreateToken(expireTime: DateTime.UtcNow.AddMinutes(-1));
        var tokens = new Mock<ITokenManager<SchemataToken>>();
        tokens.Setup(t => t.FindByReferenceIdAsync(TestCode, It.IsAny<CancellationToken>())).ReturnsAsync(token);

        var handler = CreateHandler(tokens);
        var request = CreateRequest();

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
                                                              request,
                                                              null,
                                                              CancellationToken.None
                                                          )
        );

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Code);
    }

    [Fact]
    public async Task ThrowsInvalidGrant_WhenClientIdMismatch() {
        var token  = CreateToken(clientId: "other-client");
        var tokens = new Mock<ITokenManager<SchemataToken>>();
        tokens.Setup(t => t.FindByReferenceIdAsync(TestCode, It.IsAny<CancellationToken>())).ReturnsAsync(token);

        var handler = CreateHandler(tokens);
        var request = CreateRequest();

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
                                                              request,
                                                              null,
                                                              CancellationToken.None
                                                          )
        );

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Code);
    }

    [Fact]
    public async Task ThrowsInvalidGrant_WhenRedirectUriMismatch() {
        var token  = CreateToken(redirect: "https://other.example.com/callback");
        var tokens = new Mock<ITokenManager<SchemataToken>>();
        tokens.Setup(t => t.FindByReferenceIdAsync(TestCode, It.IsAny<CancellationToken>())).ReturnsAsync(token);

        var handler = CreateHandler(tokens);
        var request = CreateRequest();

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
                                                              request,
                                                              null,
                                                              CancellationToken.None
                                                          )
        );

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Code);
    }

    [Fact]
    public async Task MarksCodeRedeemed_OnSuccessfulExchange() {
        var token  = CreateToken();
        var tokens = new Mock<ITokenManager<SchemataToken>>();
        tokens.Setup(t => t.FindByReferenceIdAsync(TestCode, It.IsAny<CancellationToken>())).ReturnsAsync(token);

        var handler = CreateHandler(tokens);
        var request = CreateRequest();

        await handler.HandleAsync(request, null, CancellationToken.None);

        tokens.Verify(t => t.UpdateAsync(token, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(TokenStatuses.Redeemed, token.Status);
    }

    [Fact]
    public async Task ReturnsSignIn_WithCorrectProperties() {
        var token  = CreateToken();
        var tokens = new Mock<ITokenManager<SchemataToken>>();
        tokens.Setup(t => t.FindByReferenceIdAsync(TestCode, It.IsAny<CancellationToken>())).ReturnsAsync(token);

        var handler = CreateHandler(tokens);
        var request = CreateRequest();

        var result = await handler.HandleAsync(request, null, CancellationToken.None);

        Assert.Equal(AuthorizationStatus.SignIn, result.Status);
        Assert.NotNull(result.Principal);
        Assert.NotNull(result.Properties);

        Assert.Equal(GrantTypes.AuthorizationCode, result.Properties[Properties.GrantType]);
        Assert.Equal(TestScope, result.Properties[Properties.Scope]);
        Assert.Equal(TestNonce, result.Properties[Properties.Nonce]);

        var identity = result.Principal!.Identity as ClaimsIdentity;
        Assert.NotNull(identity);
        Assert.Equal(SchemataAuthorizationSchemes.Bearer, identity!.AuthenticationType);
        Assert.Contains(identity.Claims, c => c.Type == Claims.ClientId && c.Value == TestClientId);
        Assert.Contains(identity.Claims, c => c.Type == Claims.Subject && c.Value == "user-1");
    }
}
