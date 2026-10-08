using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

/// <summary>
///     Issue #135: the authorization endpoint finalizes every post-validation failure from the
///     single captured callback — trusted redirect, preserved state, legal effective response
///     mode — and the renderer never guesses an encoding for an unvalidated mode.
/// </summary>
public class AuthorizeCallbackFinalizationShould
{
    private static SchemataApplication App(params string[] responseTypes) {
        return new() {
            Uid           = Guid.NewGuid(),
            ClientId      = "callback-client",
            CanonicalName = "applications/callback-client",
            ResponseTypes = [.. responseTypes],
            RedirectUris  = ["https://localhost/callback"],
        };
    }

    private static (AdviceAuthorizeClientAndRedirect<SchemataApplication> Advisor, AuthorizeContext<SchemataApplication> Context) Advisor(
        SchemataApplication app,
        string              responseType,
        string?             responseMode = null
    ) {
        var manager = new Mock<IApplicationManager<SchemataApplication>>();
        manager.Setup(m => m.FindByClientIdAsync(app.ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(app);
        manager.Setup(m => m.ValidateRedirectUriAsync(app, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((SchemataApplication _, string? uri, CancellationToken _) => uri == "https://localhost/callback");
        manager.Setup(m => m.HasResponseTypeAsync(app, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((SchemataApplication _, string? type, CancellationToken _) => type is not null && app.ResponseTypes?.Contains(type) == true);

        var options = Options.Create(new Foundation.Authentication.SchemataAuthorizationOptions {
            AllowedResponseTypes = { ResponseTypes.Code, "code id_token", ResponseTypes.IdToken },
        });

        var authz = new AuthorizeContext<SchemataApplication> {
            Application  = app,
            ResponseMode = ResponseModeService.ResolveMode(responseMode, responseType),
            Request      = new() {
                ClientId     = app.ClientId,
                RedirectUri  = "https://localhost/callback",
                State        = "the-state",
                ResponseType = responseType,
                ResponseMode = responseMode,
            },
        };

        return (new(manager.Object, options), authz);
    }

    private static async Task<OAuthException> RejectsAsync(
        AdviceAuthorizeClientAndRedirect<SchemataApplication> advisor,
        AuthorizeContext<SchemataApplication>                 authz,
        bool                                                  par = false
    ) {
        var ctx = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        if (par) {
            authz.Stage = AuthorizationRequestStage.Pushed;
        }

        using var ambient = AdviceContext.Establish(ctx);

        return await Assert.ThrowsAsync<OAuthException>(
            () => advisor.AdviseAsync(ctx, authz, CancellationToken.None));
    }

    [Fact]
    public async Task Hybrid_Rejected_For_The_Client_Uses_The_Legal_Effective_Mode_With_State() {
        // The server supports the hybrid combination; this client did not register it. The
        // request omits response_mode, so the legal effective mode is fragment (OIDC multiple
        // response types) and the state survives.
        var (advisor, authz) = Advisor(App(ResponseTypes.Code), "code id_token");

        var ex = await RejectsAsync(advisor, authz);

        Assert.Equal(OAuthErrors.UnauthorizedClient, ex.Status);
        Assert.Equal("https://localhost/callback", ex.RedirectUri);
        Assert.Equal("the-state", ex.State);
        Assert.Equal(ResponseModes.Fragment, ex.ResponseMode);
        Assert.False(ex.OmitErrorParameters);
    }

    [Fact]
    public async Task Unsupported_Server_Combination_Also_Keeps_The_Effective_Mode() {
        var (advisor, authz) = Advisor(App("code id_token"), "code token");

        var ex = await RejectsAsync(advisor, authz);

        Assert.Equal(OAuthErrors.UnsupportedResponseType, ex.Status);
        Assert.Equal(ResponseModes.Fragment, ex.ResponseMode);
        Assert.Equal("the-state", ex.State);
        Assert.False(ex.OmitErrorParameters);
    }

    [Fact]
    public async Task Unsupported_Explicit_Response_Mode_Is_A_Bare_400_Without_A_Callback() {
        var (advisor, authz) = Advisor(App(ResponseTypes.Code), ResponseTypes.Code, responseMode: "jwt");

        var ex = await RejectsAsync(advisor, authz);

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
        Assert.Null(ex.RedirectUri);
        Assert.Null(ex.State);
        Assert.Null(ex.ResponseMode);
        Assert.True(ex.OmitErrorParameters);
    }


    [Fact]
    public void Invalid_Redirect_Never_Produces_A_Callback_Fact() {
        var app = App(ResponseTypes.Code);
        var manager = new Mock<IApplicationManager<SchemataApplication>>();
        manager.Setup(m => m.FindByClientIdAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(app);
        manager.Setup(m => m.ValidateRedirectUriAsync(app, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(false);
        var options = Options.Create(new Foundation.Authentication.SchemataAuthorizationOptions {
            AllowedResponseTypes = { ResponseTypes.Code },
        });
        var authz = new AuthorizeContext<SchemataApplication> {
            Application  = app,
            ResponseMode = ResponseModes.Query,
            Request      = new() {
                ClientId = app.ClientId, RedirectUri = "https://evil.example/cb", ResponseType = ResponseTypes.Code,
            },
        };
        var advisor = new AdviceAuthorizeClientAndRedirect<SchemataApplication>(manager.Object, options);

        var ctx = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        using var ambient = AdviceContext.Establish(ctx);
        var ex = Assert.Throws<OAuthException>(
            () => advisor.AdviseAsync(ctx, authz, CancellationToken.None).GetAwaiter().GetResult());

        Assert.Null(ex.RedirectUri);
        Assert.Null(authz.Callback);
    }

    [Fact]
    public void WithCallback_Leaves_Already_Resolved_Delivery_Untouched() {
        var authz = new AuthorizeContext<SchemataApplication> {
            Callback = new("https://localhost/callback", "the-state", ResponseModes.Fragment),
        };
        var ex = new OAuthException(OAuthErrors.AccessDenied, "denied") {
            RedirectUri = "https://elsewhere.example/cb",
            State       = "other",
        };

        var result = ex.WithCallback(authz);

        Assert.Same(ex, result);
        Assert.Equal("https://elsewhere.example/cb", result.RedirectUri);
        Assert.Equal("other", result.State);
        Assert.Null(result.ResponseMode);
    }

    [Fact]
    public void Renderer_Rejects_An_Unresolved_Mode_Instead_Of_Guessing_Query() {
        Assert.Throws<InvalidOperationException>(
            () => ResponseModeService.CreateCallback("https://localhost/callback", [], "carrier-pigeon"));
    }
}
