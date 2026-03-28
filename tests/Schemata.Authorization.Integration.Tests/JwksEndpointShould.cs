using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Category", "Integration")]
public class JwksEndpointShould : IClassFixture<WebAppFactory>
{
    private readonly HttpClient _client;

    public JwksEndpointShould(WebAppFactory factory) { _client = factory.CreateClient(); }

    [Fact]
    public async Task ReturnsValidJwkSet() {
        var response = await _client.GetAsync("/.well-known/jwks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(json.RootElement.TryGetProperty("keys", out var keys));
        Assert.Equal(JsonValueKind.Array, keys.ValueKind);
    }

    [Fact]
    public async Task ContainsPublicKeyOnly() {
        var response = await _client.GetAsync("/.well-known/jwks");
        var json     = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var keys     = json.RootElement.GetProperty("keys");

        // RFC 7517: private key fields must not appear in a public JWK Set
        string[] privateFields = ["d", "p", "q", "dp", "dq", "qi"];

        foreach (var key in keys.EnumerateArray()) {
            foreach (var field in privateFields) {
                Assert.False(key.TryGetProperty(field, out var _), $"JWK should not contain private key field '{field}'");
            }
        }
    }

    [Fact]
    public async Task HasRequiredFields() {
        var response = await _client.GetAsync("/.well-known/jwks");
        var json     = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var keys     = json.RootElement.GetProperty("keys");

        Assert.True(keys.GetArrayLength() > 0, "JWKS must contain at least one key");

        var key = keys.EnumerateArray().First();

        Assert.True(key.TryGetProperty("kty", out var _));
        Assert.True(key.TryGetProperty("alg", out var _));
        Assert.True(key.TryGetProperty("use", out var use));
        Assert.Equal("sig", use.GetString());
    }
}
