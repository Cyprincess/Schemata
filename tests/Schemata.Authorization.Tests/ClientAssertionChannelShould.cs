using Schemata.Authorization.Foundation.Services;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class ClientAssertionChannelShould
{
    private const string Issuer   = "https://issuer.example";
    private const string ParUri   = Issuer + Endpoints.Par;
    private const string TokenUri = Issuer + Endpoints.Token;

    [Fact]
    public void Return_An_Empty_List_When_Issuer_Is_Not_Configured() {
        var audiences = new ClientAssertionChannel().Audiences(new());

        Assert.Empty(audiences);
    }

    [Fact]
    public void Return_Issuer_And_Token_Endpoint_As_The_Base_Audiences() {
        var audiences = new ClientAssertionChannel().Audiences(new() { Issuer = Issuer });

        Assert.Equal(new[] { Issuer, TokenUri }, audiences);
    }

    [Fact]
    public void Append_The_Explicit_Endpoint_Audience_To_The_Base_List() {
        var audiences = new ClientAssertionChannel().Audiences(new() { Issuer = Issuer }, ParUri);

        Assert.Equal(new[] { Issuer, TokenUri, ParUri }, audiences);
    }

    [Fact]
    public void Deduplicate_When_The_Explicit_Audience_Equals_The_Token_Endpoint() {
        var audiences = new ClientAssertionChannel().Audiences(new() { Issuer = Issuer }, TokenUri);

        Assert.Equal(new[] { Issuer, TokenUri }, audiences);
    }
}