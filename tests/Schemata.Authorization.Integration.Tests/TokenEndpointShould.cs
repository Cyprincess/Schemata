using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Component")]
public class TokenEndpointShould : IClassFixture<WebAppFactory>
{
    private readonly HttpClient _client;
    private readonly WebAppFactory _factory;

    public TokenEndpointShould(WebAppFactory factory) {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    [Fact]
    public async Task ClientCredentials_ReturnsPersistedCanonicalAccessToken() {
        var content = new FormUrlEncodedContent(new Dictionary<string, string> {
            ["grant_type"] = "client_credentials", ["client_id"] = "test-client", ["client_secret"] = "test-secret",
        });

        var response = await _client.PostAsync("/connect/token", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = json.RootElement;

        Assert.True(root.TryGetProperty("access_token", out var accessToken));
        Assert.Equal("Bearer", root.GetProperty("token_type").GetString());
        Assert.True(root.TryGetProperty("expires_in", out var expiresIn));
        Assert.True(expiresIn.GetInt32() > 0);

        using var scope = _factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationDbContext>>();
        await using var context = await factory.CreateDbContextAsync();
        var token = await context.Tokens.SingleAsync(t => t.ReferenceId == accessToken.GetString());

        Assert.False(string.IsNullOrWhiteSpace(token.Name));
        Assert.Equal($"tokens/{token.Name}", token.CanonicalName);
    }

    [Fact]
    public async Task InvalidGrantType_ReturnsError() {
        var content = new FormUrlEncodedContent(new Dictionary<string, string> {
            ["grant_type"] = "unsupported_grant", ["client_id"] = "test-client", ["client_secret"] = "test-secret",
        });

        var response = await _client.PostAsync("/connect/token", content);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(json.RootElement.TryGetProperty("error", out var _));
    }

    [Fact]
    public async Task InvalidClientCredentials_ReturnsError() {
        var content = new FormUrlEncodedContent(new Dictionary<string, string> {
            ["grant_type"] = "client_credentials", ["client_id"] = "test-client", ["client_secret"] = "wrong-secret",
        });

        var response = await _client.PostAsync("/connect/token", content);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(json.RootElement.TryGetProperty("error", out var _));
    }
}
