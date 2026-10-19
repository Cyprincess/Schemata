using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Component")]
public class DiscoveryEndpointShould : IClassFixture<WebAppFactory>
{
    private readonly HttpClient _client;

    public DiscoveryEndpointShould(WebAppFactory factory) { _client = factory.CreateClient(); }

    [Fact]
    public async Task ReturnsValidJson_AtDiscoveryEndpoint() {
        var response = await _client.GetAsync("/.well-known/openid-configuration");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Discovery_ContainsRequiredFields() {
        var response = await _client.GetAsync("/.well-known/openid-configuration");
        var json     = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root     = json.RootElement;

        Assert.True(root.TryGetProperty("issuer", out var _));
        Assert.True(root.TryGetProperty("token_endpoint", out var _));
        Assert.True(root.TryGetProperty("jwks_uri", out var _));
        Assert.True(root.TryGetProperty("response_types_supported", out var _));
        Assert.True(root.TryGetProperty("subject_types_supported", out var _));
        Assert.True(root.TryGetProperty("id_token_signing_alg_values_supported", out var _));
    }

    [Fact]
    public async Task Discovery_WithEnabledGrantFlows_AdvertisesClientCredentialsAndRefreshToken() {
        var response = await _client.GetAsync("/.well-known/openid-configuration");
        var json     = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var grants   = json.RootElement.GetProperty("grant_types_supported");

        Assert.Contains(grants.EnumerateArray(), grant => grant.GetString() == "client_credentials");
        Assert.Contains(grants.EnumerateArray(), grant => grant.GetString() == "refresh_token");
    }

    [Fact]
    public async Task Issuer_MatchesConfiguration() {
        var response = await _client.GetAsync("/.well-known/openid-configuration");
        var json     = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var issuer   = json.RootElement.GetProperty("issuer").GetString();

        Assert.Equal("https://localhost", issuer);
    }

    [Fact]
    public async Task Root_Oidc_OAuth_And_Jwks_Publish_Equal_Issuer_And_Endpoint_Fields() {
        var oidcResponse  = await _client.GetAsync("/.well-known/openid-configuration");
        var oauthResponse = await _client.GetAsync("/.well-known/oauth-authorization-server");
        var jwksResponse  = await _client.GetAsync("/.well-known/jwks");

        Assert.Equal(HttpStatusCode.OK, oidcResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, oauthResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, jwksResponse.StatusCode);

        using var oidc  = await JsonDocument.ParseAsync(await oidcResponse.Content.ReadAsStreamAsync());
        using var oauth = await JsonDocument.ParseAsync(await oauthResponse.Content.ReadAsStreamAsync());
        using var jwks  = await JsonDocument.ParseAsync(await jwksResponse.Content.ReadAsStreamAsync());

        var issuer = oidc.RootElement.GetProperty("issuer").GetString();
        Assert.False(string.IsNullOrWhiteSpace(issuer));
        Assert.Equal(issuer, oauth.RootElement.GetProperty("issuer").GetString());
        Assert.Equal($"{issuer}/.well-known/jwks", oidc.RootElement.GetProperty("jwks_uri").GetString());
        Assert.Equal(oidc.RootElement.GetProperty("jwks_uri").GetString(), oauth.RootElement.GetProperty("jwks_uri").GetString());

        var oidcGrants = oidc.RootElement.GetProperty("grant_types_supported").EnumerateArray()
                              .Select(value => value.GetString()).OrderBy(value => value);
        var oauthGrants = oauth.RootElement.GetProperty("grant_types_supported").EnumerateArray()
                                .Select(value => value.GetString()).OrderBy(value => value);
        Assert.Equal(oidcGrants, oauthGrants);
        Assert.True(jwks.RootElement.GetProperty("keys").GetArrayLength() > 0);
    }
}
