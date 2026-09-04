using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class AdviceClaimsAudienceShould
{
    private static AdviceContext Ctx() => new(new ServiceCollection().BuildServiceProvider());

    private static AdviceClaimsAudience Create(string? defaultResource = null) {
        return new(Options.Create(new SchemataAuthorizationOptions {
            Issuer = "https://as.example", DefaultResource = defaultResource,
        }));
    }

    [Fact]
    public async Task Mint_Both_Audiences_Tagged_By_Destination() {
        var advisor = Create("https://api.example");
        var claims = new List<Claim> {
            new(IdentityClaims.Subject, "user-1"),
            new(Claims.ClientId,        "client-1"),
        };

        await advisor.AdviseAsync(Ctx(), claims, CancellationToken.None);

        var at = claims.Single(c => c.Type == Claims.Audience && c.Properties.ContainsKey(ClaimDestinations.AccessToken));
        Assert.Equal("https://api.example", at.Value);
        var id = claims.Single(c => c.Type == Claims.Audience && c.Properties.ContainsKey(ClaimDestinations.IdentityToken));
        Assert.Equal("client-1", id.Value);
    }

    [Fact]
    public async Task Fall_Back_To_The_Issuer_When_No_Default_Resource_Is_Configured() {
        var advisor = Create();
        var claims = new List<Claim> { new(Claims.ClientId, "client-1") };

        await advisor.AdviseAsync(Ctx(), claims, CancellationToken.None);

        Assert.Contains(claims, c => c.Type == Claims.Audience && c.Value == "https://as.example");
    }

    [Fact]
    public async Task Skip_The_Id_Token_Audience_Without_A_Client_Id() {
        var advisor = Create();
        var claims = new List<Claim> { new(IdentityClaims.Subject, "user-1") };

        await advisor.AdviseAsync(Ctx(), claims, CancellationToken.None);

        Assert.Contains(claims, c => c.Type == Claims.Audience && c.Value == "https://as.example");
        Assert.DoesNotContain(claims, c => c.Type == Claims.Audience && c.Value is "client-1");
    }

    [Fact]
    public async Task Preserve_An_Explicit_Audience() {
        var advisor = Create();
        var claims = new List<Claim> {
            new(Claims.Audience, "https://explicit.example"),
            new(Claims.ClientId, "client-1"),
        };

        await advisor.AdviseAsync(Ctx(), claims, CancellationToken.None);

        Assert.Single(claims, c => c.Type == Claims.Audience);
    }
}

public class AdviceDestinationSubjectShould
{
    private static AdviceContext Ctx() => new(new ServiceCollection().BuildServiceProvider());

    [Fact]
    public async Task Skip_Claims_Already_Tagged_With_A_Destination() {
        var advisor     = new AdviceDestinationSubject();
        var pretagged   = new Claim(Claims.Audience, "client-1");
        pretagged.Properties[ClaimDestinations.IdentityToken] = Parameters.Token;
        var destinations = new HashSet<string>();

        var result = await advisor.AdviseAsync(Ctx(), pretagged, destinations, new(), CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Empty(destinations);
        Assert.Single(pretagged.Properties, kv => kv.Key == ClaimDestinations.IdentityToken);
    }

    [Fact]
    public async Task Route_Untagged_Audience_Claims_To_Both_Token_Destinations() {
        var advisor      = new AdviceDestinationSubject();
        var claim        = new Claim(Claims.Audience, "https://as.example");
        var destinations = new HashSet<string>();

        var result = await advisor.AdviseAsync(Ctx(), claim, destinations, new(), CancellationToken.None);

        Assert.Equal(AdviseResult.Handle, result);
        Assert.Contains(ClaimDestinations.AccessToken, destinations);
        Assert.Contains(ClaimDestinations.IdentityToken, destinations);
    }
}
