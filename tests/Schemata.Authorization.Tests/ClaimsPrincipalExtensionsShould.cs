using System.Security.Claims;
using Schemata.Authorization.Foundation.Extensions;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class ClaimsPrincipalExtensionsShould
{
    private static ClaimsPrincipal Principal(params Claim[] claims) {
        return new(new ClaimsIdentity(claims));
    }

    [Fact]
    public void Honor_A_Scope_Carried_By_Any_Scope_Claim() {
        // Both claim orders: a space-separated claim beside an exact single-scope claim.
        var separatedFirst = Principal(
            new(Claims.Scope, "openid profile"),
            new(Claims.Scope, "email"));
        var exactFirst = Principal(
            new(Claims.Scope, "email"),
            new(Claims.Scope, "openid profile"));

        foreach (var principal in new[] { separatedFirst, exactFirst }) {
            Assert.True(principal.HasScope("openid"));
            Assert.True(principal.HasScope("profile"));
            Assert.True(principal.HasScope("email"));
            Assert.False(principal.HasScope("address"));
        }
    }
}
