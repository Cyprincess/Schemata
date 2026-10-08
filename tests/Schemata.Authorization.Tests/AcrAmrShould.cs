using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class AcrAmrShould
{
    private static readonly AuthenticationContext Context = new(
        "urn:schemata:acr:classes:multifactor", ["pwd", "otp"], 1767225600);

    private static (AdviceContext Ctx, AuthorizationClaimContext Issuance) Ctx(AuthenticationContext? context) {
        var ctx = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var issuance = new AuthorizationClaimContext {
            Grant = new AuthorizationGrantContext {
                Subject        = "users/u-1",
                SubjectKind    = GrantSubjectKinds.EndUser,
                Source         = GrantTypes.AuthorizationCode,
                Authentication = context,
            },
        };
        return (ctx, issuance);
    }

    private static AdviceClaimsAuthenticationContext Create() => new();

    [Fact]
    public async Task Mint_Acr_Amr_And_AuthTime_Tagged_For_Both_Destinations() {
        var claims = new List<Claim> { new(IdentityClaims.Subject, "users/u-1") };
        var (ctx, issuance) = Ctx(Context);

        await Create().AdviseAsync(ctx, claims, issuance, CancellationToken.None);

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
        var claims = new List<Claim> { new(IdentityClaims.Subject, "users/u-1") };
        var (ctx, issuance) = Ctx(new(null, ["pwd", "otp"], null));

        await Create().AdviseAsync(ctx, claims, issuance, CancellationToken.None);

        var amr = claims.Single(c => c.Type == Claims.Amr);
        Assert.Equal("""["pwd","otp"]""", amr.Value);
        Assert.Equal(JsonClaimValueTypes.Json, amr.ValueType);
        Assert.DoesNotContain(claims, c => c.Type is Claims.Acr or Claims.AuthTime);
    }

    [Fact]
    public async Task Mint_Nothing_And_Publish_Nothing_When_The_Context_Is_Empty() {
        var claims = new List<Claim> { new(IdentityClaims.Subject, "users/u-1") };
        var (ctx, issuance) = Ctx(new(null, [], null));

        await Create().AdviseAsync(ctx, claims, issuance, CancellationToken.None);

        Assert.Single(claims);
    }

    [Fact]
    public async Task Replace_Bare_Context_Claims_With_The_Persisted_Event() {
        var claims = new List<Claim> {
            new(IdentityClaims.Subject, "users/u-1"),
            new(Claims.Acr, "stale"),
            new(Claims.Amr, """["sms"]"""),
            new(Claims.AuthTime, "1"),
        };
        var (ctx, issuance) = Ctx(Context);

        await Create().AdviseAsync(ctx, claims, issuance, CancellationToken.None);

        Assert.Equal(4, claims.Count);
        Assert.Equal(Context.Acr, claims.Single(c => c.Type == Claims.Acr).Value);
        Assert.Equal("""["pwd","otp"]""", claims.Single(c => c.Type == Claims.Amr).Value);
        Assert.Equal("1767225600", claims.Single(c => c.Type == Claims.AuthTime).Value);
        Assert.All(
            claims.Where(c => c.Type is Claims.Acr or Claims.Amr or Claims.AuthTime),
            claim => Assert.True(claim.Properties.ContainsKey(ClaimDestinations.AccessToken)));
    }

}
