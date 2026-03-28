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
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Tests;

public class AdviceAuthorizeResponseTypeShould
{
    private static (AdviceAuthorizeClientAndRedirect<SchemataApplication> advisor, AdviceContext ctx) Create(params string[] allowedTypes) {
        var opts = new SchemataAuthorizationOptions();
        foreach (var t in allowedTypes) opts.AllowedResponseTypes.Add(t);
        opts.AllowedResponseModes.Add(ResponseModes.Query);
        opts.AllowedResponseModes.Add(ResponseModes.Fragment);

        var app = new SchemataApplication { ClientId = "test" };

        var manager = new Mock<IApplicationManager<SchemataApplication>>();
        manager.Setup(m => m.FindByCanonicalNameAsync("test", It.IsAny<CancellationToken>()))
               .ReturnsAsync(app);
        manager.Setup(m => m.ValidateRedirectUriAsync(app, "https://example.com/cb", It.IsAny<CancellationToken>()))
               .ReturnsAsync(true);

        var sp      = new ServiceCollection().BuildServiceProvider();
        var advisor = new AdviceAuthorizeClientAndRedirect<SchemataApplication>(manager.Object, Options.Create(opts));
        var ctx     = new AdviceContext(sp);
        return (advisor, ctx);
    }

    private static AuthorizeRequest Req(string responseType, string? responseMode = null) {
        return new AuthorizeRequest {
            ClientId     = "test",
            RedirectUri  = "https://example.com/cb",
            ResponseType = responseType,
            ResponseMode = responseMode,
            Nonce        = "n",
        };
    }

    private static AuthorizeContext<SchemataApplication> Authz(AuthorizeRequest request) {
        return new AuthorizeContext<SchemataApplication> { Request = request };
    }

    [Fact]
    public async Task AcceptAllowedResponseType() {
        var (advisor, ctx) = Create("code");

        var result = await advisor.AdviseAsync(ctx, Authz(Req("code")));

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task NormalizeResponseTypeOrder() {
        var (advisor, ctx) = Create("code id_token");

        var request = Req("id_token code");
        await advisor.AdviseAsync(ctx, Authz(request));

        Assert.Equal("code id_token", request.ResponseType);
    }

    [Fact]
    public async Task RejectUnsupportedResponseType() {
        var (advisor, ctx) = Create("code");

        var ex = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(ctx, Authz(Req("token"))));
        Assert.Equal(OAuthErrors.UnsupportedResponseType, ex.Code);
    }

    [Fact]
    public async Task RejectUnsupportedResponseMode() {
        var (advisor, ctx) = Create("code");

        var ex = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(ctx, Authz(Req("code", "form_post"))));
        Assert.Equal(OAuthErrors.InvalidRequest, ex.Code);
    }

    [Fact]
    public async Task AcceptEmptyResponseMode() {
        var (advisor, ctx) = Create("code");

        var result = await advisor.AdviseAsync(ctx, Authz(Req("code")));

        Assert.Equal(AdviseResult.Continue, result);
    }
}
