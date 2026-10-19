using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;
using Schemata.Authorization.Skeleton.Services;

namespace Schemata.Authorization.Tests;

public class AdviceAuthorizeResponseTypeShould
{
    private static (AdviceAuthorizeClientAndRedirect<SchemataApplication> advisor, AdviceContext ctx) Create(
        params string[] allowedTypes
    ) {
        return Create(allowedTypes, allowedTypes);
    }

    private static (AdviceAuthorizeClientAndRedirect<SchemataApplication> advisor, AdviceContext ctx) Create(
        string[] allowedTypes,
        string[] registeredTypes
    ) {
        var opts = new SchemataAuthorizationOptions();
        foreach (var t in allowedTypes) opts.AllowedResponseTypes.Add(t);
        opts.AllowedResponseModes.Add(ResponseModes.Query);
        opts.AllowedResponseModes.Add(ResponseModes.Fragment);

        var app = new SchemataApplication {
            ClientId = "test",
            // The client's registered response_types metadata is the runtime authority for each
            // combination it may use; registration stores exactly these entries.
            ResponseTypes = [..registeredTypes],
        };

        var manager = new Mock<IApplicationManager<SchemataApplication>>();
        manager.SetupTypedMetadata();

        manager.Setup(m => m.FindByClientIdAsync("test", It.IsAny<CancellationToken>())).ReturnsAsync(app);
        manager.Setup(m => m.ValidateRedirectUriAsync(app, "https://example.com/cb", It.IsAny<CancellationToken>()))
               .ReturnsAsync(true);

        var sp      = new ServiceCollection().BuildServiceProvider();
        var advisor = new AdviceAuthorizeClientAndRedirect<SchemataApplication>(manager.Object, Options.Create(opts));
        var ctx     = new AdviceContext(sp);
        return (advisor, ctx);
    }

    private static AuthorizeRequest Req(string responseType, string? responseMode = null) {
        return new() {
            ClientId     = "test",
            RedirectUri  = "https://example.com/cb",
            ResponseType = responseType,
            ResponseMode = responseMode,
            Nonce        = "n",
        };
    }

    private static AuthorizeContext<SchemataApplication> Authz(AuthorizeRequest request) {
        return new() { Request = request };
    }

    [Fact]
    public async Task Accept_AllowedResponseType() {
        var (advisor, ctx) = Create("code");

        var result = await advisor.AdviseAsync(ctx, Authz(Req("code")));

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task Normalize_ResponseTypeOrder() {
        var (advisor, ctx) = Create("code id_token");

        var request = Req("id_token code");
        await advisor.AdviseAsync(ctx, Authz(request));

        Assert.Equal("code id_token", request.ResponseType);
    }

    [Fact]
    public async Task Reject_UnsupportedResponseType() {
        var (advisor, ctx) = Create("code");

        var ex = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(ctx, Authz(Req("token"))));
        Assert.Equal(OAuthErrors.UnsupportedResponseType, ex.Status);
    }

    [Fact]
    public async Task Reject_ACombinationTheClientDidNotRegister_EvenWhenTheServerAllowsIt() {
        var (advisor, ctx) = Create(["code", "code id_token"], ["code"]);

        // The server allows the hybrid, but this client only registered r:code.
        var ex = await Assert.ThrowsAsync<OAuthException>(() =>
                                                              advisor.AdviseAsync(ctx, Authz(Req("code id_token"))));
        Assert.Equal(OAuthErrors.UnauthorizedClient, ex.Status);
    }

    [Fact]
    public async Task Accept_ARegisteredCombination_RegardlessOfRegisteredTokenOrder() {
        var (advisor, ctx) = Create("code id_token");

        // Registration stored the entry with a different token order than the request; both
        // normalize to the same combination.
        var result = await advisor.AdviseAsync(ctx, Authz(Req("id_token code")));

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task Reject_UnsupportedResponseMode() {
        var (advisor, ctx) = Create("code");

        var ex = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(
                                                              ctx, Authz(Req("code", "form_post"))));
        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
    }

    [Fact]
    public async Task Accept_EmptyResponseMode() {
        var (advisor, ctx) = Create("code");

        var result = await advisor.AdviseAsync(ctx, Authz(Req("code")));

        Assert.Equal(AdviseResult.Continue, result);
    }
}
