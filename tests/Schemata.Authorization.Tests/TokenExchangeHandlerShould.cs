using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Tests;

public class TokenExchangeHandlerShould
{
    private static readonly SchemataApplication TestApp = new() { Id = 1, ClientId = "test-client" };

    private static TokenExchangeHandler<SchemataApplication> CreateHandler(
        Mock<IClientAuthenticationService<SchemataApplication>> clientAuth,
        params (string tokenType, Mock<ITokenExchangeHandler<SchemataApplication>> mock)[] subHandlers
    ) {
        var services = new ServiceCollection();
        foreach (var (tokenType, mock) in subHandlers) {
            services.AddKeyedScoped<ITokenExchangeHandler<SchemataApplication>>(tokenType, (_, _) => mock.Object);
        }

        var handler = new TokenExchangeHandler<SchemataApplication>(clientAuth.Object, services.BuildServiceProvider());
        return handler;
    }

    private static Mock<IClientAuthenticationService<SchemataApplication>> MockClientAuth() {
        var mock = new Mock<IClientAuthenticationService<SchemataApplication>>();
        mock.Setup(c => c.AuthenticateAsync(It.IsAny<Dictionary<string, List<string?>>?>(), It.IsAny<Dictionary<string, List<string?>>?>(), It.IsAny<Dictionary<string, List<string?>>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestApp);
        return mock;
    }

    [Fact]
    public async Task ThrowInvalidRequest_WhenSubjectTokenEmpty() {
        var clientAuth = MockClientAuth();
        var handler    = CreateHandler(clientAuth);
        var request = new TokenRequest {
            GrantType        = GrantTypes.TokenExchange,
            SubjectToken     = "",
            SubjectTokenType = "urn:ietf:params:oauth:token-type:access_token",
        };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Code);
    }

    [Fact]
    public async Task ThrowInvalidRequest_WhenSubjectTokenTypeEmpty() {
        var clientAuth = MockClientAuth();
        var handler    = CreateHandler(clientAuth);
        var request = new TokenRequest {
            GrantType = GrantTypes.TokenExchange, SubjectToken = "some-token", SubjectTokenType = "",
        };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Code);
    }

    [Fact]
    public async Task ThrowInvalidRequest_WhenSubjectTokenTypeNotSupported() {
        var clientAuth = MockClientAuth();
        var handler    = CreateHandler(clientAuth);

        var request = new TokenRequest {
            GrantType = GrantTypes.TokenExchange, SubjectToken = "ref-1", SubjectTokenType = "urn:unknown:type",
        };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Code);
    }

    [Fact]
    public async Task DelegatesToMatchingSubHandler() {
        const string tokenType = "urn:ietf:params:oauth:token-type:access_token";
        var          expected  = AuthorizationResult.Content(new { });

        var subHandler = new Mock<ITokenExchangeHandler<SchemataApplication>>();
        subHandler.SetupGet(h => h.SubjectTokenType).Returns(tokenType);
        subHandler.Setup(h => h.HandleAsync(It.IsAny<SchemataApplication>(), It.IsAny<TokenRequest>(), It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(expected);

        var clientAuth = MockClientAuth();
        var handler    = CreateHandler(clientAuth, (tokenType, subHandler));

        var request = new TokenRequest {
            GrantType = GrantTypes.TokenExchange, SubjectToken = "ref-1", SubjectTokenType = tokenType,
        };

        var result = await handler.HandleAsync(request, null, CancellationToken.None);

        Assert.Same(expected, result);
        subHandler.Verify(h => h.HandleAsync(TestApp, request, null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
