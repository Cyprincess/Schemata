using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Tests;

public class AdviceAuthorizeNonceShould
{
    private static (AdviceAuthorizeNonce<SchemataApplication> advisor, AdviceContext ctx) Create(bool requireNonce = true) {
        var opts    = new CodeFlowOptions { RequireNonce = requireNonce };
        var sp      = new ServiceCollection().BuildServiceProvider();
        var advisor = new AdviceAuthorizeNonce<SchemataApplication>(Options.Create(opts));
        var ctx     = new AdviceContext(sp);
        return (advisor, ctx);
    }

    [Fact]
    public async Task RequireNonceForIdTokenResponseType() {
        var (advisor, ctx) = Create();
        var authz = new AuthorizeContext<SchemataApplication> { Request = new AuthorizeRequest { ResponseType = "code id_token" } };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(ctx, authz));
        Assert.Equal(OAuthErrors.InvalidRequest, ex.Code);
    }

    [Fact]
    public async Task AcceptIdTokenResponseTypeWithNonce() {
        var (advisor, ctx) = Create();
        var authz = new AuthorizeContext<SchemataApplication> { Request = new AuthorizeRequest { ResponseType = "code id_token", Nonce = "abc" } };

        var result = await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task SkipNonceCheckWhenNotRequired() {
        var (advisor, ctx) = Create(false);
        var authz = new AuthorizeContext<SchemataApplication> { Request = new AuthorizeRequest { ResponseType = "code id_token" } };

        var result = await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task ContinueWhenResponseTypeHasNoIdToken() {
        var (advisor, ctx) = Create();
        var authz = new AuthorizeContext<SchemataApplication> { Request = new AuthorizeRequest { ResponseType = "code" } };

        var result = await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(AdviseResult.Continue, result);
    }
}
