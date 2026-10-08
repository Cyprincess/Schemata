using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Integration.Tests;

/// <summary>
///     Behavioral coverage for the OAuth 2.0 Device Authorization Grant composed through
///     <c>UseDeviceFlow()</c>: with the feature installed the device endpoint, discovery metadata,
///     user-code interaction, and the keyed token-endpoint dispatch agree, and an approved device
///     code exchanges for tokens; without the feature the endpoint and discovery metadata stay
///     absent. Polling states follow RFC 8628 §3.5.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Layer", "Component")]
public class DeviceFlowShould : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public DeviceFlowShould(WebAppFactory factory) { _factory = factory; }

    private WebAppFactory DeviceFlow() { return _factory.WithEnvironment("DeviceFlow"); }

    [Fact]
    public async Task Stay_Absent_From_Endpoints_And_Discovery_Without_UseDeviceFlow() {
        using var client = _factory.CreateClient();

        var device = await client.PostAsync("/connect/device", DeviceAuthorizeForm("device-secret"));
        Assert.Equal(HttpStatusCode.NotFound, device.StatusCode);

        var discovery = await client.GetAsync("/.well-known/openid-configuration");
        Assert.Equal(HttpStatusCode.OK, discovery.StatusCode);
        var document = await discovery.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(document.TryGetProperty("device_authorization_endpoint", out _));
        if (document.TryGetProperty("grant_types_supported", out var grants)) {
            Assert.DoesNotContain(grants.EnumerateArray(), g => g.GetString() == GrantTypes.DeviceCode);
        }
    }

    [Fact]
    public async Task Advertise_The_Device_Flow_In_Discovery_With_UseDeviceFlow() {
        using var factory = DeviceFlow();
        using var client  = factory.CreateClient();

        var discovery = await client.GetAsync("/.well-known/openid-configuration");
        Assert.Equal(HttpStatusCode.OK, discovery.StatusCode);
        var document = await discovery.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(document.TryGetProperty("device_authorization_endpoint", out var endpoint));
        Assert.Equal("https://localhost/Connect/Device", endpoint.GetString());
        Assert.Contains(
            document.GetProperty("grant_types_supported").EnumerateArray(),
            g => g.GetString() == GrantTypes.DeviceCode);
    }

    [Fact]
    public async Task Issue_A_Device_And_User_Code_Pair_With_UseDeviceFlow() {
        using var factory = DeviceFlow();
        using var client  = factory.CreateClient();

        var response = await client.PostAsync("/connect/device", DeviceAuthorizeForm("device-secret"));
        Assert.True(HttpStatusCode.OK == response.StatusCode,
            $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        var pair = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(pair.GetProperty("device_code").GetString()));
        var userCode = pair.GetProperty("user_code").GetString();
        Assert.False(string.IsNullOrWhiteSpace(userCode));
        Assert.Equal("https://localhost/device", pair.GetProperty("verification_uri").GetString());
        var complete = pair.GetProperty("verification_uri_complete").GetString();
        Assert.NotNull(complete);
        Assert.StartsWith("https://localhost/device?", complete);
        Assert.Contains(userCode, complete);
        Assert.True(pair.GetProperty("expires_in").GetInt32() > 0);
        Assert.True(pair.GetProperty("interval").GetInt32() > 0);
    }

    [Fact]
    public async Task Reject_Device_Authorization_With_Bad_Client_Credentials() {
        using var factory = DeviceFlow();
        using var client  = factory.CreateClient();

        var response = await client.PostAsync("/connect/device", DeviceAuthorizeForm("wrong-secret"));

        // RFC 6749 §5.2 permits 401 for invalid_client whenever the server names its supported
        // HTTP authentication schemes (mandatory when the client attempted the Authorization
        // header); this host answers 400 for a failed client_secret_post attempt.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(OAuthErrors.InvalidClient, await Error(response));
    }

    [Fact]
    public async Task Answer_Polling_With_Authorization_Pending_Before_Approval() {
        using var factory = DeviceFlow();
        using var client  = factory.CreateClient();

        var (deviceCode, _) = await RequestDeviceCodes(client);

        var poll = await client.PostAsync("/connect/token", PollForm(deviceCode));
        Assert.Equal(HttpStatusCode.BadRequest, poll.StatusCode);
        Assert.Equal(OAuthErrors.AuthorizationPending, await Error(poll));
    }

    [Fact]
    public async Task Answer_Polling_With_Access_Denied_After_Denial() {
        using var factory = DeviceFlow();
        using var client  = factory.CreateClient();

        var (deviceCode, userCode) = await RequestDeviceCodes(client);

        var deny = await client.DeleteAsync(
            $"/connect/interact?user_code={userCode}&code_type={Uri.EscapeDataString(TokenTypeUris.UserCode)}");
        Assert.Equal(HttpStatusCode.NoContent, deny.StatusCode);

        var poll = await client.PostAsync("/connect/token", PollForm(deviceCode));
        Assert.Equal(HttpStatusCode.BadRequest, poll.StatusCode);
        Assert.Equal(OAuthErrors.AccessDenied, await Error(poll));
    }

    [Fact]
    public async Task Answer_Polling_With_Expired_Token_After_The_Device_Code_Expires() {
        var       time    = new FakeTimeProvider();
        using var factory = DeviceFlow().WithServices(services => services.AddSingleton<TimeProvider>(time));
        using var client  = factory.CreateClient();

        var (deviceCode, _) = await RequestDeviceCodes(client);

        time.Advance(TimeSpan.FromMinutes(16));

        var poll = await client.PostAsync("/connect/token", PollForm(deviceCode));
        Assert.Equal(HttpStatusCode.BadRequest, poll.StatusCode);
        Assert.Equal(OAuthErrors.ExpiredToken, await Error(poll));
    }

    [Fact]
    public async Task Exchange_An_Approved_Device_Code_For_Tokens() {
        using var factory = DeviceFlow().WithServices(ConfigureInteractionAuthentication);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new(InteractionAuthenticationHandler.SchemeName);

        var (deviceCode, userCode) = await RequestDeviceCodes(client);

        var approve = await client.PostAsync("/connect/interact", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["user_code"] = userCode,
            ["code_type"] = TokenTypeUris.UserCode,
        }));
        Assert.Equal(HttpStatusCode.NoContent, approve.StatusCode);

        var poll = await client.PostAsync("/connect/token", PollForm(deviceCode));
        Assert.True(HttpStatusCode.OK == poll.StatusCode,
            $"{(int)poll.StatusCode}: {await poll.Content.ReadAsStringAsync()}");

        var tokens = await poll.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(tokens.GetProperty("access_token").GetString()));
        Assert.Equal("Bearer", tokens.GetProperty("token_type").GetString());
    }

    private static FormUrlEncodedContent DeviceAuthorizeForm(string secret) {
        return new(new Dictionary<string, string> {
            ["client_id"]     = "device-client",
            ["client_secret"] = secret,
            ["scope"]         = "api",
        });
    }

    private static FormUrlEncodedContent PollForm(string deviceCode) {
        return new(new Dictionary<string, string> {
            ["grant_type"]    = GrantTypes.DeviceCode,
            ["device_code"]   = deviceCode,
            ["client_id"]     = "device-client",
            ["client_secret"] = "device-secret",
        });
    }

    private static async Task<(string DeviceCode, string UserCode)> RequestDeviceCodes(HttpClient client) {
        var response = await client.PostAsync("/connect/device", DeviceAuthorizeForm("device-secret"));
        Assert.True(HttpStatusCode.OK == response.StatusCode,
            $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var pair = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (pair.GetProperty("device_code").GetString()!, pair.GetProperty("user_code").GetString()!);
    }

    private static async Task<string?> Error(HttpResponseMessage response) {
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        return document.GetProperty("error").GetString();
    }

    private static void ConfigureInteractionAuthentication(IServiceCollection services) {
        services.AddAuthentication(InteractionAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, InteractionAuthenticationHandler>(
                    InteractionAuthenticationHandler.SchemeName, _ => { });
    }

    /// <summary>Authenticates the resource owner for the interaction approval POST.</summary>
    private sealed class InteractionAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory                               logger,
        UrlEncoder                                   encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "ManagementTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
            if (Request.Headers.Authorization != SchemeName) {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new(IdentityClaims.Subject, "users/u-1"),
            ], SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new(principal, SchemeName)));
        }
    }
}
