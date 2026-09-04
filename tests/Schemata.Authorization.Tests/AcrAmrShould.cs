using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Skeleton.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class AcrAmrShould
{
    private static readonly AuthenticationContext Context = new(
        "urn:schemata:acr:classes:multifactor", ["pwd", "otp"], 1767225600);

    private static AdviceContext Ctx() => new(new ServiceCollection().BuildServiceProvider());

    private static AdviceClaimsAuthenticationContext Create(AuthenticationContext context) {
        var provider = new Mock<IAuthenticationContextProvider>();
        provider.Setup(p => p.GetContextAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(context);
        return new(provider.Object);
    }

    [Fact]
    public async Task Mint_Acr_Amr_And_AuthTime_Tagged_For_Both_Destinations() {
        var advisor = Create(Context);
        var claims  = new List<Claim> { new(IdentityClaims.Subject, "users/u-1") };

        await advisor.AdviseAsync(Ctx(), claims, CancellationToken.None);

        Assert.Equal(4, claims.Count);
        foreach (var type in new[] { Claims.Acr, Claims.Amr, Claims.AuthTime }) {
            var claim = claims.Single(c => c.Type == type);
            Assert.True(claim.Properties.ContainsKey(ClaimDestinations.AccessToken), type);
            Assert.True(claim.Properties.ContainsKey(ClaimDestinations.IdentityToken), type);
        }

        Assert.Equal("1767225600", claims.Single(c => c.Type == Claims.AuthTime).Value);
    }

    [Fact]
    public async Task Mint_The_Amr_Claim_As_A_Json_Array() {
        var advisor = Create(new(null, ["pwd", "otp"], null));
        var claims  = new List<Claim> { new(IdentityClaims.Subject, "users/u-1") };

        await advisor.AdviseAsync(Ctx(), claims, CancellationToken.None);

        var amr = claims.Single(c => c.Type == Claims.Amr);
        Assert.Equal("""["pwd","otp"]""", amr.Value);
        Assert.Equal(JsonClaimValueTypes.Json, amr.ValueType);
        Assert.DoesNotContain(claims, c => c.Type is Claims.Acr or Claims.AuthTime);
    }

    [Fact]
    public async Task Mint_Nothing_And_Publish_Nothing_When_The_Context_Is_Empty() {
        var advisor = Create(new(null, [], null));
        var claims  = new List<Claim> { new(IdentityClaims.Subject, "users/u-1") };
        var ctx     = Ctx();

        await advisor.AdviseAsync(ctx, claims, CancellationToken.None);

        Assert.Single(claims);
        Assert.False(ctx.TryGet<AuthenticationContext>(out var _));
    }

    [Fact]
    public async Task Replace_Bare_Context_Claims_With_Minted_Ones() {
        var advisor = Create(Context);
        var claims  = new List<Claim> {
            new(IdentityClaims.Subject, "users/u-1"),
            new(Claims.Acr, "urn:schemata:acr:classes:multifactor"),
            new(Claims.Amr, """["pwd","otp"]"""),
            new(Claims.AuthTime, "1767225600"),
        };

        await advisor.AdviseAsync(Ctx(), claims, CancellationToken.None);

        Assert.Equal(4, claims.Count);
        Assert.Single(claims, c => c.Type == Claims.Acr);
        Assert.Single(claims, c => c.Type == Claims.Amr);
        Assert.Single(claims, c => c.Type == Claims.AuthTime);
        Assert.All(
            claims.Where(c => c.Type is Claims.Acr or Claims.Amr or Claims.AuthTime),
            claim => Assert.True(claim.Properties.ContainsKey(ClaimDestinations.AccessToken)));
    }

    [Fact]
    public async Task Resolve_The_Context_From_The_Assembled_Principal_Claims() {
        ClaimsPrincipal? received = null;
        var provider = new Mock<IAuthenticationContextProvider>();
        provider.Setup(p => p.GetContextAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
                .Callback<ClaimsPrincipal, CancellationToken>((principal, _) => received = principal)
                .ReturnsAsync(Context);
        var advisor = new AdviceClaimsAuthenticationContext(provider.Object);
        var claims  = new List<Claim> {
            new(IdentityClaims.Subject, "users/u-1"),
            new(Claims.Amr, """["pwd","otp"]"""),
            new(Claims.Acr, "urn:schemata:acr:classes:multifactor"),
            new(Claims.AuthTime, "1767225600"),
        };

        await advisor.AdviseAsync(Ctx(), claims, CancellationToken.None);

        Assert.NotNull(received);
        Assert.Equal("""["pwd","otp"]""", received!.FindFirstValue(Claims.Amr));
        Assert.Equal("urn:schemata:acr:classes:multifactor", received.FindFirstValue(Claims.Acr));
        Assert.Equal("1767225600", received.FindFirstValue(Claims.AuthTime));
    }

    [Fact]
    public async Task Publish_The_Minted_Context_For_Code_Persistence() {
        var advisor = Create(Context);
        var claims  = new List<Claim> { new(IdentityClaims.Subject, "users/u-1") };
        var ctx     = Ctx();

        await advisor.AdviseAsync(ctx, claims, CancellationToken.None);

        Assert.True(ctx.TryGet<AuthenticationContext>(out var published));
        Assert.Equal(Context, published);
    }
}
