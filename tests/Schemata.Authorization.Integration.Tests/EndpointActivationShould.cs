using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Authorization.Integration.Tests;

/// <summary>
///     Behavioral coverage for feature-driven Connect endpoint activation, per issue #139:
///     an optional capability's actions are absent from MVC endpoint discovery when its
///     feature is not installed (404, no dispatch), shared endpoints stay when any owner
///     remains, and an installed capability keeps its actions, policy, and discovery.
/// </summary>
[Trait("Category", "Integration")]
public class EndpointActivationShould : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public EndpointActivationShould(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Keep_Active_Token_Route_When_Its_Handler_Is_Misconfigured() {
        using var factory = _factory.WithServices(services => services.RemoveAll<TokenEndpoint>());
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/Connect/Token", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["grant_type"] = "client_credentials",
        }));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    private WebApplicationFactory<Program> WithoutUserInfo() {
        return _factory.WithWebHostBuilder(builder => builder.UseEnvironment("NoUserInfo"));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task Profile_Absent_Without_UseUserInfo(string method) {
        using var factory = WithoutUserInfo();
        using var client  = factory.CreateClient();

        var request  = new HttpRequestMessage(new(method), "/Connect/Profile");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Userinfo_Discovery_Absent_Without_UseUserInfo() {
        using var factory = WithoutUserInfo();
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/.well-known/openid-configuration");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(document.TryGetProperty("userinfo_endpoint", out _));
    }

    [Fact]
    public async Task Token_Endpoint_Remains_When_Other_Flows_Are_Installed() {
        using var factory = WithoutUserInfo();
        using var client  = factory.CreateClient();

        var response = await client.PostAsync("/Connect/Token", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
        }));

        // The route exists (owned by the still-installed code/refresh/client-credentials flows)
        // and the dispatch runs: this host keys no handler for the device grant, so the answer
        // is the exact RFC 6749 §5.2 error, never 404/405/500.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("unsupported_grant_type", document.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Token_Absent_And_NotAdvertised_When_No_Flow_Owner_Is_Installed() {
        using var factory = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("NoFlows"));
        using var client  = factory.CreateClient();

        var token = await client.PostAsync("/Connect/Token", new StringContent(string.Empty));
        Assert.Equal(HttpStatusCode.NotFound, token.StatusCode);

        var oidc = await client.GetAsync("/.well-known/openid-configuration");
        Assert.Equal(HttpStatusCode.NotFound, oidc.StatusCode);
        var discovery = await client.GetAsync("/.well-known/oauth-authorization-server");
        Assert.Equal(HttpStatusCode.OK, discovery.StatusCode);
        var document = await discovery.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(document.TryGetProperty("token_endpoint", out _));
        Assert.False(document.TryGetProperty("grant_types_supported", out _));
        Assert.False(document.TryGetProperty("code_challenge_methods_supported", out _));
        Assert.False(document.TryGetProperty("response_modes_supported", out _));
        Assert.False(document.TryGetProperty("scopes_supported", out _));
    }

    [Fact]
    public async Task Profile_Absent_When_No_Flow_Owner_Is_Installed() {
        using var factory = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("NoFlows"));
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/Connect/Profile");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }


    [Theory]
    [InlineData("NoFlows", false, false)]
    [InlineData("Testing", false, true)]
    [InlineData("Jar", true, true)]
    public async Task Par_And_Jar_Discovery_Fields_Reflect_Feature_Activation(
        string environment,
        bool   expectRequest,
        bool   expectRequestUri
    ) {
        using var factory = _factory.WithWebHostBuilder(builder => builder.UseEnvironment(environment));
        using var client  = factory.CreateClient();

        var response = await client.GetAsync(environment == "NoFlows"
            ? "/.well-known/oauth-authorization-server" : "/.well-known/openid-configuration");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(expectRequest,
            document.TryGetProperty("request_parameter_supported", out var request) && request.GetBoolean());
        Assert.Equal(expectRequestUri,
            document.TryGetProperty("request_uri_parameter_supported", out var requestUri) && requestUri.GetBoolean());
        Assert.False(document.TryGetProperty("require_request_uri_registration", out _));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task Profile_Present_With_UseUserInfo(string method) {
        using var client = _factory.CreateClient();

        var request  = new HttpRequestMessage(new(method), "/Connect/Profile");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, value => string.Equals(value.Scheme, "Bearer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Userinfo_Discovery_Present_With_UseUserInfo() {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/.well-known/openid-configuration");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(document.TryGetProperty("userinfo_endpoint", out var endpoint));
        Assert.False(string.IsNullOrWhiteSpace(endpoint.GetString()));
    }
    [Theory]
    [InlineData("GET", "invalid_request")]
    [InlineData("POST", "login_required")]
    [InlineData("DELETE", "invalid_request")]
    public async Task Interaction_Present_With_UseEndSession_Alone(string method, string error) {
        using var factory = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("EndSessionOnly"));
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(new(method),
            "/Connect/Interact?code=missing&code_type=urn:schemata:authorization:token-type:logout");
        if (method == "POST") {
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                ["code"]      = "missing",
                ["code_type"] = "urn:schemata:authorization:token-type:logout",
            });
        }

        using var response = await client.SendAsync(request);

        // Dispatch reaches the keyed logout interaction handler: an unknown code reads as
        // invalid_request, and approval without an authenticated subject reads as login_required —
        // never 404 (route missing) or 500 (broken registration).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(error, document.GetProperty("error").GetString());
    }

}
