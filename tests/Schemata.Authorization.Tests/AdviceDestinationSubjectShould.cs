using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Models;
using Xunit;

namespace Schemata.Authorization.Tests;

public class AdviceDestinationSubjectShould
{
    private static AuthorizationClaimContext Issuance() => new();

    private static AdviceContext Ctx() => new(new ServiceCollection().BuildServiceProvider());

    [Fact]
    public async Task Skip_Claims_Already_Tagged_With_A_Destination() {
        var advisor   = new AdviceDestinationSubject();
        var pretagged = new Claim(AuthorizationConstants.Claims.Audience, "client-1") { Properties = {
                [AuthorizationConstants.ClaimDestinations.IdentityToken] = AuthorizationConstants.Parameters.Token,
            },
        };
        var destinations = new HashSet<string>();

        var result = await advisor.AdviseAsync(Ctx(), pretagged, destinations, new(), Issuance(), CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Empty(destinations);
        Assert.Single(pretagged.Properties, kv => kv.Key == AuthorizationConstants.ClaimDestinations.IdentityToken);
    }

    [Fact]
    public async Task Route_Untagged_Audience_Claims_To_Both_Token_Destinations() {
        var advisor      = new AdviceDestinationSubject();
        var claim        = new Claim(AuthorizationConstants.Claims.Audience, "https://as.example");
        var destinations = new HashSet<string>();

        var result = await advisor.AdviseAsync(Ctx(), claim, destinations, new(), Issuance(), CancellationToken.None);

        Assert.Equal(AdviseResult.Handle, result);
        Assert.Contains(AuthorizationConstants.ClaimDestinations.AccessToken, destinations);
        Assert.Contains(AuthorizationConstants.ClaimDestinations.IdentityToken, destinations);
    }
}