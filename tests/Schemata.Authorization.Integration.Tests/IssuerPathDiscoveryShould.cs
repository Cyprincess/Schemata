using System;
using System.Net;
using System.Net.Http;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Authorization.Integration.Tests;

/// <summary>
///     Path issuer (<c>https://localhost/issuer1</c>) discovery and JWKS routing, per
///     OpenID Connect Discovery 1.0 §4.2 and RFC 8414 §3.1: the well-known routes derive
///     from the configured issuer, the document names the exact issuer, and every advertised
///     endpoint URL is prefixed by it.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Layer", "Component")]
public class IssuerPathDiscoveryShould : IDisposable
{
    private const string Issuer = "https://localhost/issuer1";

    private readonly WebAppFactory _factory = new WebAppFactory().WithEnvironment("IssuerPath");

    [Theory]
    [InlineData("/issuer1/.well-known/openid-configuration")]
    [InlineData("/.well-known/oauth-authorization-server/issuer1")]
    [InlineData("/issuer1/.well-known/jwks")]
    public async Task Serve_Metadata_And_Jwks_At_The_Issuer_Derived_Paths(string path) {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/.well-known/openid-configuration")]
    [InlineData("/.well-known/jwks")]
    [InlineData("/issuer1/.well-known/oauth-authorization-server")]
    [InlineData("/.well-known/openid-configuration/issuer1")]
    public async Task Not_Serve_The_Root_Or_Wrongly_Transformed_Paths(string path) {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/issuer1/.well-known/openid-configuration")]
    [InlineData("/.well-known/oauth-authorization-server/issuer1")]
    public async Task Publish_The_Exact_Issuer_And_Issuer_Prefixed_Endpoints_On_Both_Metadata_Routes(string path) {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(path);
        var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root     = document.RootElement;

        Assert.Equal(Issuer, root.GetProperty("issuer").GetString());
        Assert.Equal($"{Issuer}/Connect/Authorize", root.GetProperty("authorization_endpoint").GetString());
        Assert.Equal($"{Issuer}/Connect/Token", root.GetProperty("token_endpoint").GetString());
        Assert.Equal($"{Issuer}/Connect/Par", root.GetProperty("pushed_authorization_request_endpoint").GetString());
        Assert.Equal($"{Issuer}/.well-known/jwks", root.GetProperty("jwks_uri").GetString());
    }

    [Theory]
    [InlineData("/issuer1/.well-known/openid-configuration")]
    [InlineData("/.well-known/oauth-authorization-server/issuer1")]
    public async Task Publish_The_Same_Installed_Capabilities_On_Both_Metadata_Routes(string path) {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(path);
        var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var grants   = document.RootElement.GetProperty("grant_types_supported");

        Assert.Contains(grants.EnumerateArray(), grant => grant.GetString() == "authorization_code");
        Assert.Contains(grants.EnumerateArray(), grant => grant.GetString() == "client_credentials");
    }

    [Fact]
    public async Task Dispatch_The_Advertised_Token_Endpoint_At_The_Issuer_Path() {
        using var client = _factory.CreateClient();
        using var discovery = JsonDocument.Parse(await client.GetStringAsync("/issuer1/.well-known/openid-configuration"));
        var endpoint = new Uri(discovery.RootElement.GetProperty("token_endpoint").GetString()!).PathAndQuery;
        using var response = await client.PostAsync(endpoint, new FormUrlEncodedContent(new Dictionary<string, string> {
            ["grant_type"] = "unsupported-smoke-grant",
        }));
        using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("unsupported_grant_type", error.RootElement.GetProperty("error").GetString());
        using var rootResponse = await client.PostAsync("/Connect/Token", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["grant_type"] = "unsupported-smoke-grant",
        }));
        Assert.Equal(HttpStatusCode.NotFound, rootResponse.StatusCode);
    }
    public void Dispose() { _factory.Dispose(); }

}
