using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class ParShould
{
    private const string ClientId   = "client-1";
    private const string Reference  = "par-reference";
    private const string RequestUri = "urn:ietf:params:oauth:request_uri:" + Reference;

    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Recover_And_Redeem_A_Valid_Par_Request_Once() {
        var app    = Application();
        var stored = Token(app, Now.AddMinutes(1));
        var tokens = new Mock<ITokenStore<SchemataToken>>();
        tokens.Setup(value => value.FindByReferenceIdAsync(Reference, It.IsAny<CancellationToken>()))
              .ReturnsAsync(stored);
        tokens.Setup(value => value.TryRedeemAsync(stored, It.IsAny<CancellationToken>()))
              .ReturnsAsync(true);
        var advisor = Advisor(app, tokens);
        var authz   = Context();

        var result = await advisor.AdviseAsync(Advice(), authz);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Same(app, authz.Application);
        Assert.Equal(ClientId, authz.Request!.ClientId);
        Assert.Equal("https://client.example/callback", authz.Request.RedirectUri);
        Assert.Equal("code", authz.Request.ResponseType);
        Assert.Equal("openid profile", authz.Request.Scope);
        Assert.Equal("preserved-state", authz.Request.State);
        Assert.Equal(RequestUri, authz.Request.RequestUri);
        tokens.Verify(value => value.TryRedeemAsync(stored, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reject_An_Already_Redeemed_Par_Request() {
        var app    = Application();
        var stored = Token(app, Now.AddMinutes(1));
        stored.Status = TokenStatuses.Redeemed;
        var tokens = Tokens(stored);
        var advisor = Advisor(app, tokens);

        var exception = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(Advice(), Context()));

        Assert.Equal(OAuthErrors.InvalidRequestUri, exception.Status);
        tokens.Verify(value => value.TryRedeemAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reject_An_Expired_Par_Request() {
        var app     = Application();
        var advisor = Advisor(app, Tokens(Token(app, Now)));

        var exception = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(Advice(), Context()));

        Assert.Equal(OAuthErrors.InvalidRequestUri, exception.Status);
    }

    [Fact]
    public async Task Reject_A_Par_Request_Issued_To_Another_Client() {
        var app   = Application();
        var token = Token(app, Now.AddMinutes(1));
        token.Application = "applications/client-2";
        var advisor = Advisor(app, Tokens(token));

        var exception = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(Advice(), Context()));

        Assert.Equal(OAuthErrors.InvalidRequestUri, exception.Status);
    }

    [Fact]
    public async Task Require_Par_When_The_Authorization_Server_Requires_It() {
        var app     = Application();
        var options = new PushedAuthorizationRequestsOptions { RequireForAllClients = true };
        var advisor = Advisor(app, new Mock<ITokenStore<SchemataToken>>(), options);
        var authz = new AuthorizeContext<SchemataApplication> {
            Request = new() { ClientId = ClientId, ResponseType = "code" },
        };

        var exception = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(Advice(), authz));

        Assert.Equal(OAuthErrors.InvalidRequest, exception.Status);
    }

    [Fact]
    public async Task Require_Par_When_Only_The_Client_Requires_It() {
        var app = Application();
        app.RequirePushedAuthorizationRequests = true;
        var advisor = Advisor(app, new Mock<ITokenStore<SchemataToken>>(), new() {
            RequireForAllClients = false,
        });
        var authz = new AuthorizeContext<SchemataApplication> {
            Request = new() { ClientId = ClientId, ResponseType = "code" },
        };

        var exception = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(Advice(), authz));

        Assert.Equal(OAuthErrors.InvalidRequest, exception.Status);
    }

    [Fact]
    public async Task Reject_A_Request_Containing_Request_And_Request_Uri() {
        var app     = Application();
        var advisor = Advisor(app, new Mock<ITokenStore<SchemataToken>>());
        var authz = new AuthorizeContext<SchemataApplication> {
            Request = new() { ClientId = ClientId, Request = "request-object", RequestUri = RequestUri },
        };

        var exception = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(Advice(), authz));

        Assert.Equal(OAuthErrors.InvalidRequest, exception.Status);
    }

    [Fact]
    public async Task Reject_A_Non_Par_Request_Uri() {
        var app     = Application();
        var advisor = Advisor(app, new Mock<ITokenStore<SchemataToken>>());
        var authz = new AuthorizeContext<SchemataApplication> {
            Request = new() { ClientId = ClientId, RequestUri = "https://client.example/request.jwt" },
        };

        var exception = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(Advice(), authz));

        Assert.Equal(OAuthErrors.RequestUriNotSupported, exception.Status);
    }


    [Fact]
    public async Task Store_The_Authenticated_Raw_Form_As_A_Par_Request_And_Return_Its_Handle() {
        var app  = Application();
        var form = new Dictionary<string, List<string?>> {
            [Parameters.ClientId]     = [ClientId],
            [Parameters.ClientSecret] = ["secret"],
            [Parameters.Scope]        = ["openid profile"],
        };
        var client = new Mock<IClientAuthenticationService<SchemataApplication>>();
        client.Setup(value => value.AuthenticateAsync(null, form, null, It.IsAny<CancellationToken>()))
              .ReturnsAsync(app);
        SchemataToken? stored = null;
        var tokens = new Mock<ITokenStore<SchemataToken>>();
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
              .Callback((SchemataToken? token, CancellationToken _) => stored = token)
              .ReturnsAsync((SchemataToken? token, CancellationToken _) => token);
        var options = new PushedAuthorizationRequestsOptions { Lifetime = TimeSpan.FromSeconds(90) };
        var handler = new ParHandler<SchemataApplication>(
            client.Object,
            tokens.Object,
            Options.Create(options),
            Options.Create(new JsonSerializerOptions()),
            new FakeTimeProvider(Now));
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var ambient  = AdviceContext.Establish(new(provider));
        var request = new AuthorizeRequest {
            ClientId     = ClientId,
            RedirectUri  = "https://client.example/callback",
            ResponseType = "code",
            Scope        = "openid profile",
            State        = "state-1",
        };

        var result = await handler.ParAsync(request, form, null, CancellationToken.None);

        Assert.Equal(AuthorizationStatus.Content, result.Status);
        var response = Assert.IsType<PushedAuthorizationResponse>(result.Data);
        Assert.StartsWith("urn:ietf:params:oauth:request_uri:", response.RequestUri);
        Assert.Equal(90, response.ExpiresIn);
        Assert.NotNull(stored);
        Assert.Equal(TokenTypes.ParRequest, stored.Type);
        Assert.Equal(TokenTypes.ParRequest, stored.Provider);
        Assert.Equal(TokenStatuses.Valid, stored.Status);
        Assert.Equal(TokenFormats.Reference, stored.Format);
        Assert.Equal(response.RequestUri!["urn:ietf:params:oauth:request_uri:".Length..], stored.ReferenceId);
        Assert.Equal(app.CanonicalName, stored.Application);
        Assert.Equal(Now.UtcDateTime.AddSeconds(90), stored.ExpireTime);
        var payload = JsonSerializer.Deserialize<AuthorizeRequest>(stored.Payload!);
        Assert.Equal(ClientId, payload!.ClientId);
        Assert.Equal("https://client.example/callback", payload.RedirectUri);
        Assert.Equal("openid profile", payload.Scope);
        Assert.Equal("state-1", payload.State);
        client.Verify(value => value.AuthenticateAsync(null, form, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static AdviceAuthorizeRequestUri<SchemataApplication> Advisor(
        SchemataApplication app,
        Mock<ITokenStore<SchemataToken>> tokens,
        PushedAuthorizationRequestsOptions? options = null
    ) {
        var apps = new Mock<IApplicationManager<SchemataApplication>>();
        apps.Setup(value => value.FindByClientIdAsync(ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(app);
        return new(
            apps.Object,
            tokens.Object,
            Options.Create(options ?? new PushedAuthorizationRequestsOptions()),
            Options.Create(new JsonSerializerOptions()),
            new FakeTimeProvider(Now));
    }

    private static Mock<ITokenStore<SchemataToken>> Tokens(SchemataToken token) {
        var tokens = new Mock<ITokenStore<SchemataToken>>();
        tokens.Setup(value => value.FindByReferenceIdAsync(Reference, It.IsAny<CancellationToken>()))
              .ReturnsAsync(token);
        return tokens;
    }

    private static SchemataApplication Application() {
        return new() {
            Uid           = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ClientId      = ClientId,
            CanonicalName = "applications/" + ClientId,
        };
    }

    private static SchemataToken Token(SchemataApplication app, DateTimeOffset expiry) {
        return new() {
            Uid         = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Application = app.CanonicalName,
            Provider    = TokenTypes.ParRequest,
            Type        = TokenTypes.ParRequest,
            Status      = TokenStatuses.Valid,
            Format      = TokenFormats.Reference,
            ReferenceId = Reference,
            ExpireTime  = expiry.UtcDateTime,
            Payload = JsonSerializer.Serialize(new AuthorizeRequest {
                ClientId     = ClientId,
                RedirectUri  = "https://client.example/callback",
                ResponseType = "code",
                Scope        = "openid profile",
                State        = "preserved-state",
            }),
        };
    }

    private static AuthorizeContext<SchemataApplication> Context() {
        return new() { Request = new() { ClientId = ClientId, RequestUri = RequestUri } };
    }

    private static AdviceContext Advice() {
        return new(new ServiceCollection().BuildServiceProvider());
    }
}

public class AuthorizationRequestRepresentationShould
{
    [Fact]
    public async Task Reject_An_Unhandled_Request_Object() {
        var advisor = new AdviceAuthorizationRequestRepresentation<SchemataApplication>();
        var context = new AuthorizeContext<SchemataApplication> {
            Request = new AuthorizeRequest { Request = "request-object" },
        };

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            advisor.AdviseAsync(new AdviceContext(new ServiceCollection().BuildServiceProvider()), context));

        Assert.Equal(OAuthErrors.RequestNotSupported, exception.Status);
    }

    [Fact]
    public async Task Reject_An_Unhandled_Request_Uri() {
        var advisor = new AdviceAuthorizationRequestRepresentation<SchemataApplication>();
        var context = new AuthorizeContext<SchemataApplication> {
            Request = new AuthorizeRequest { RequestUri = "urn:example:request" },
        };

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            advisor.AdviseAsync(new AdviceContext(new ServiceCollection().BuildServiceProvider()), context));

        Assert.Equal(OAuthErrors.RequestUriNotSupported, exception.Status);
    }
}
