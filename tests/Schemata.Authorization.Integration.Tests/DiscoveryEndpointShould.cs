using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Category", "Integration")]
public class DiscoveryEndpointShould : IClassFixture<WebAppFactory>
{
    private readonly HttpClient _client;

    public DiscoveryEndpointShould(WebAppFactory factory) { _client = factory.CreateClient(); }

    [Fact]
    public async Task ReturnsValidJson() {
        var response = await _client.GetAsync("/.well-known/openid-configuration");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ContainsRequiredFields() {
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
    public async Task IssuerMatchesConfiguration() {
        var response = await _client.GetAsync("/.well-known/openid-configuration");
        var json     = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var issuer   = json.RootElement.GetProperty("issuer").GetString();

        Assert.Equal("https://localhost", issuer);
    }
}
