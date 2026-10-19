using Schemata.Authorization.Skeleton.Entities;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class DpopConstantsShould
{
    [Fact]
    public void Register_The_Rfc_9449_Error_Codes() {
        Assert.Equal("invalid_dpop_proof", OAuthErrors.InvalidDpopProof);
        Assert.Equal("use_dpop_nonce",     OAuthErrors.UseDpopNonce);
    }

    [Fact]
    public void Register_The_Rfc_9449_Wire_Constants() {
        Assert.Equal("dpop+jwt",   TokenMediaTypes.DpopJwt);
        Assert.Equal("dpop_jkt",   Parameters.DpopJkt);
        Assert.Equal("DPoP",       Schemes.Dpop);
        Assert.Equal("DPoP",       Headers.Dpop);
        Assert.Equal("DPoP-Nonce", Headers.DpopNonce);
        Assert.Equal("cnf",        Claims.Cnf);
        Assert.Equal("jkt",        Claims.Jkt);
    }

    [Fact]
    public void Default_Dpop_Bound_Access_Tokens_To_False() {
        Assert.False(new SchemataApplication().DpopBoundAccessTokens);
    }
}
