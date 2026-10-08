using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Primitives;
using Schemata.Authorization.Foundation.Binding;
using Moq;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class TokenExchangeHandlerShould
{
    private static readonly SchemataApplication
        TestApp = new() { Uid = Guid.NewGuid(), ClientId = "test-client" };

    private static (TokenExchangeHandler<SchemataApplication> Handler, IServiceProvider Sp) CreateHandler(
        Mock<IClientAuthenticationService<SchemataApplication>>                 clientAuth,
        params (string key, ITokenExchangeHandler<SchemataApplication> handler)[] handlers
    ) {
        var services = new ServiceCollection();
        foreach (var (key, sub) in handlers) {
            services.AddKeyedScoped<ITokenExchangeHandler<SchemataApplication>>(key, (_, _) => sub);
        }

        var sp = services.BuildServiceProvider();
        return (new(clientAuth.Object, sp), sp);
    }

    private static Mock<IClientAuthenticationService<SchemataApplication>> MockClientAuth() {
        var mock = new Mock<IClientAuthenticationService<SchemataApplication>>();
        mock.Setup(c => c.AuthenticateAsync(It.IsAny<Dictionary<string, List<string?>>?>(),
                                            It.IsAny<Dictionary<string, List<string?>>?>(),
                                            It.IsAny<Dictionary<string, List<string?>>?>(),
                                            It.IsAny<CancellationToken>(),
                                            It.IsAny<string?>()))
            .ReturnsAsync(new ClientAuthenticationResult<SchemataApplication> { Application = TestApp, Method = ClientAuthMethods.ClientSecretPost, Authenticated = true });
        return mock;
    }

    [Fact]
    public async Task ThrowInvalidRequest_WhenSubjectTokenEmpty() {
        var clientAuth = MockClientAuth();
        var (handler, sp) = CreateHandler(clientAuth);
        using var ambient = AdviceContext.Establish(new(sp));
        var request = new TokenRequest {
            GrantType        = GrantTypes.TokenExchange,
            SubjectToken     = "",
            SubjectTokenType = "urn:ietf:params:oauth:token-type:access_token",
        };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
                                                              request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
    }

    [Fact]
    public async Task ThrowInvalidRequest_WhenSubjectTokenTypeEmpty() {
        var clientAuth = MockClientAuth();
        var (handler, sp) = CreateHandler(clientAuth);
        using var ambient = AdviceContext.Establish(new(sp));
        var request = new TokenRequest {
            GrantType = GrantTypes.TokenExchange, SubjectToken = "some-token", SubjectTokenType = "",
        };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
                                                              request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
    }

    [Fact]
    public async Task ThrowInvalidRequest_WhenSubjectTokenTypeNotSupported() {
        var clientAuth = MockClientAuth();
        var (handler, sp) = CreateHandler(clientAuth);
        using var ambient = AdviceContext.Establish(new(sp));

        var request = new TokenRequest {
            GrantType = GrantTypes.TokenExchange, SubjectToken = "ref-1", SubjectTokenType = "urn:unknown:type",
        };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
                                                              request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
        var detail = Assert.IsType<ErrorInfoDetail>(Assert.Single(ex.Details!));
        Assert.Equal(Parameters.SubjectTokenType, detail.Metadata?["value"]);
    }

    [Fact]
    public async Task ThrowInvalidRequest_WhenRequestedTokenTypeNotSupported() {
        var clientAuth = MockClientAuth();
        var (handler, sp) = CreateHandler(clientAuth);
        using var ambient = AdviceContext.Establish(new(sp));

        var request = new TokenRequest {
            GrantType          = GrantTypes.TokenExchange,
            SubjectToken       = "ref-1",
            SubjectTokenType   = TokenTypeUris.AccessToken,
            RequestedTokenType = "urn:unknown:type",
        };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
                                                              request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
        var detail = Assert.IsType<ErrorInfoDetail>(Assert.Single(ex.Details!));
        Assert.Equal(Parameters.RequestedTokenType, detail.Metadata?["value"]);
    }
    [Fact]
    public async Task ThrowInvalidRequest_WhenActorTokenMissingType() {
        var clientAuth = MockClientAuth();
        var (handler, sp) = CreateHandler(clientAuth);
        using var ambient = AdviceContext.Establish(new(sp));

        var request = new TokenRequest {
            GrantType        = GrantTypes.TokenExchange,
            SubjectToken     = "ref-1",
            SubjectTokenType = TokenTypeUris.AccessToken,
            ActorToken       = "actor-1",
        };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
                                                              request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
    }

    [Fact]
    public async Task ThrowInvalidRequest_WhenActorTokenTypeMissingToken() {
        var clientAuth = MockClientAuth();
        var (handler, sp) = CreateHandler(clientAuth);
        using var ambient = AdviceContext.Establish(new(sp));

        var request = new TokenRequest {
            GrantType        = GrantTypes.TokenExchange,
            SubjectToken     = "ref-1",
            SubjectTokenType = TokenTypeUris.AccessToken,
            ActorTokenType   = TokenTypeUris.AccessToken,
        };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
                                                              request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
    }

    [Fact]
    public async Task Delegates_WhenRequestedTokenTypeStandard() {
        var tokenType  = TokenTypeUris.AccessToken;
        var expected   = AuthorizationResult.Content(new { });
        var subHandler = new Mock<ITokenExchangeHandler<SchemataApplication>>();
        subHandler.SetupGet(h => h.SubjectTokenType).Returns(tokenType);
        subHandler.Setup(h => h.HandleAsync(It.IsAny<SchemataApplication>(), It.IsAny<TokenRequest>(),
                                            It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(expected);

        var clientAuth = MockClientAuth();
        var (handler, sp) = CreateHandler(clientAuth, (tokenType, subHandler.Object));
        using var ambient = AdviceContext.Establish(new(sp));

        var request = new TokenRequest {
            GrantType          = GrantTypes.TokenExchange,
            SubjectToken       = "ref-1",
            SubjectTokenType   = tokenType,
            RequestedTokenType = TokenTypeUris.RefreshToken,
        };

        var result = await handler.HandleAsync(request, null, CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task Delegates_ToMatchingSubHandler() {
        const string tokenType = "urn:ietf:params:oauth:token-type:access_token";
        var          expected  = AuthorizationResult.Content(new { });

        var subHandler = new Mock<ITokenExchangeHandler<SchemataApplication>>();
        subHandler.SetupGet(h => h.SubjectTokenType).Returns(tokenType);
        subHandler.Setup(h => h.HandleAsync(It.IsAny<SchemataApplication>(), It.IsAny<TokenRequest>(),
                                            It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(expected);

        var clientAuth = MockClientAuth();
        var (handler, sp) = CreateHandler(clientAuth, (tokenType, subHandler.Object));
        using var ambient = AdviceContext.Establish(new(sp));

        var request = new TokenRequest {
            GrantType = GrantTypes.TokenExchange, SubjectToken = "ref-1", SubjectTokenType = tokenType,
        };

        var result = await handler.HandleAsync(request, null, CancellationToken.None);

        Assert.Same(expected, result);
        subHandler.Verify(h => h.HandleAsync(TestApp, request, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RejectCustomRequestedType_WhenOnlySubjectFallbackRegistered() {
        const string subjectType = "urn:example:token-type:session";
        var          fallback    = new Mock<ITokenExchangeHandler<SchemataApplication>>();
        fallback.SetupGet(h => h.SubjectTokenType).Returns(subjectType);
        fallback.Setup(h => h.HandleAsync(It.IsAny<SchemataApplication>(), It.IsAny<TokenRequest>(),
                                          It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(AuthorizationResult.Content(new { }));

        var clientAuth = MockClientAuth();
        var (handler, sp) = CreateHandler(clientAuth, (subjectType, fallback.Object));
        using var ambient = AdviceContext.Establish(new(sp));

        var request = new TokenRequest {
            GrantType          = GrantTypes.TokenExchange,
            SubjectToken       = "ref-1",
            SubjectTokenType   = subjectType,
            RequestedTokenType = DelegationProfileHandler.RequestedType,
        };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
                                                              request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
        var detail = Assert.IsType<ErrorInfoDetail>(Assert.Single(ex.Details!));
        Assert.Equal(Parameters.RequestedTokenType, detail.Metadata?["value"]);
        fallback.Verify(h => h.HandleAsync(It.IsAny<SchemataApplication>(), It.IsAny<TokenRequest>(),
                                           It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DelegateToExactComposite_WhenRequestedTokenTypeCustom() {
        var clientAuth = MockClientAuth();
        var (handler, sp) = CreateHandler(
            clientAuth,
            ($"{DelegationProfileHandler.SubjectType}|{DelegationProfileHandler.RequestedType}",
             new DelegationProfileHandler()));
        using var ambient = AdviceContext.Establish(new(sp));

        var http = new DefaultHttpContext();
        http.Request.ContentType = "application/x-www-form-urlencoded";
        http.Request.Form = new FormCollection(new Dictionary<string, StringValues> {
            [Parameters.GrantType] = GrantTypes.TokenExchange,
            [Parameters.SubjectToken] = DelegationProfileHandler.ExpectedSubjectToken,
            [Parameters.SubjectTokenType] = DelegationProfileHandler.SubjectType,
            [Parameters.RequestedTokenType] = DelegationProfileHandler.RequestedType,
            [Parameters.Audience] = new([DelegationProfileHandler.ExpectedAudience, "contacts"]),
            [Parameters.Resource] = new([DelegationProfileHandler.ExpectedResource, "https://contacts.example"]),
            [Parameters.Scope] = DelegationProfileHandler.ExpectedScope,
        });
        var binding = new Mock<ModelBindingContext>();
        binding.SetupGet(b => b.HttpContext).Returns(http);
        binding.SetupProperty(b => b.Result);
        await new OAuthFormBinder<TokenRequest>().BindModelAsync(binding.Object);
        var request = Assert.IsType<TokenRequest>(binding.Object.Result.Model);

        var result = await handler.HandleAsync(request, null, CancellationToken.None);

        Assert.Same(DelegationProfileHandler.Success, result);
    }

    // Test-only exchange profile: validates its own subject token, audience, resource, and scope
    // before issuing, demonstrating that request policy lives in each keyed profile.
    private sealed class DelegationProfileHandler : ITokenExchangeHandler<SchemataApplication>
    {
        public const string SubjectType          = "urn:example:token-type:session";
        public const string RequestedType        = "urn:example:token-type:delegation";
        public const string ExpectedSubjectToken = "session-token-1";
        public const string ExpectedAudience     = "https://api.example";
        public const string ExpectedResource     = "https://resource.example/api";
        public const string ExpectedScope        = "delegation.read";

        public static readonly AuthorizationResult Success =
            AuthorizationResult.Content(new { issued_token_type = RequestedType });

        public string SubjectTokenType => SubjectType;

        public Task<AuthorizationResult> HandleAsync(
            SchemataApplication application,
            TokenRequest        request,
            ClaimsPrincipal?    principal,
            CancellationToken   ct
        ) {
            if (!string.Equals(request.SubjectToken, ExpectedSubjectToken, StringComparison.Ordinal)
             || request.Audience is null || !request.Audience.SequenceEqual(new[] { ExpectedAudience, "contacts" })
             || request.Resource is null || !request.Resource.SequenceEqual(new[] { ExpectedResource, "https://contacts.example" })
             || !string.Equals(request.Scope, ExpectedScope, StringComparison.Ordinal)) {
                throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.NOT_SUPPORTED, new Dictionary<string, string?> { ["value"] = Parameters.SubjectToken });
            }

            return Task.FromResult(Success);
        }
    }
}
