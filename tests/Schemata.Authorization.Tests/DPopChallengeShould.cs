using System.Globalization;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class DPopChallengeShould
{
    private static readonly string[] Algs = ["RS256", "ES256"];

    private static string Challenge(string? error, params string[] algorithms) {
        return SchemataAuthenticationHandler<SchemataApplication>.BuildChallenge(error, algorithms);
    }

    [Fact]
    public void Omit_Error_And_Algs_When_Neither_Applies() {
        Assert.Equal(Schemes.Dpop, Challenge(null));
    }

    [Fact]
    public void Keep_Algs_But_Omit_Error_For_A_Challenge_Without_Credentials() {
        Assert.Equal("DPoP algs=\"ES256 RS256\"", Challenge(string.Empty, Algs));
    }

    [Fact]
    public void Attach_Error_Alone_When_No_Algorithms_Are_Configured() {
        Assert.Equal($"DPoP error=\"{OAuthErrors.InvalidToken}\"", Challenge(OAuthErrors.InvalidToken));
    }

    [Fact]
    public void Attach_Error_And_Algs_For_A_Rejected_Token() {
        Assert.Equal(
            $"DPoP error=\"{OAuthErrors.UseDpopNonce}\", algs=\"ES256 RS256\"",
            Challenge(OAuthErrors.UseDpopNonce, Algs));
    }

    [Fact]
    public void Order_Algs_Ordinally_Regardless_Of_Configuration_Order() {
        Assert.Equal("DPoP algs=\"ES256 PS256 RS256\"", Challenge(null, "RS256", "PS256", "ES256"));
    }

    [Fact]
    public void Ride_The_Error_On_The_Bearer_Companion_Challenge() {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        try {
            Assert.Equal(
                $"Bearer error=\"{OAuthErrors.InvalidToken}\", error_description=\"Invalid token\"",
                SchemataAuthenticationHandler<SchemataApplication>.BuildBearerChallenge(OAuthErrors.InvalidToken));
        } finally {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
