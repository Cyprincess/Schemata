using System;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.WebEncoders;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Integration.Tests;

/// <summary>
///     Issue #131 transferred #73 acceptance over real HTTP: a client dynamically registered with
///     <c>token_endpoint_auth_method=none</c> completes the authorization-code + PKCE exchange and
///     a subsequent refresh through the existing DI channel collection, presenting its
///     <c>client_id</c> alone — no secret exists and none is fabricated — while the same client is
///     refused as a client_credentials authority.
/// </summary>
public class DcrNoneClientFlowShould(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private const string Verifier  = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
    private const string Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

    [Fact]
    public async Task None_Client_Completes_Code_Pkce_And_Refresh_Without_A_Secret() {
        // One host instance holds both legs: in-memory storage means the registered client and
        // the code flow must share the same process and database.
        using var flow = factory.WithEnvironment("Authenticated").WithServices(Configure_Interaction_Authentication);
        using var client  = flow.CreateClient(new() { AllowAutoRedirect = false });

        // Dynamic registration: mint the none client through the real register endpoint.
        var clientId = await RegisterNoneClientAsync(client);

        // Endpoint permissions are administrator-granted (entity contract): after the dynamic
        // registration the host admin grants the new client the connect endpoints, exactly as
        // the seeded fixtures receive them.
        using (var scope = flow.Services.CreateScope()) {
            var manager = scope.ServiceProvider.GetRequiredService<Skeleton.Managers.IApplicationManager<Skeleton.Entities.SchemataApplication>>();
            var entity  = await manager.FindByClientIdAsync(clientId, System.Threading.CancellationToken.None);
            Assert.NotNull(entity);
            entity.Permissions = ["e:/Connect/Authorize", "e:/Connect/Token"];
            await manager.UpdateAsync(entity, System.Threading.CancellationToken.None);
        }

        client.DefaultRequestHeaders.Authorization = new(InteractionAuthenticationHandler.SchemeName);

        var authorize = await client.GetAsync("/connect/authorize?client_id=" + clientId
            + "&redirect_uri=https%3A%2F%2Flocalhost%2Fcallback"
            + "&response_type=code"
            + "&code_challenge=" + Challenge
            + "&code_challenge_method=S256"
            + "&scope=openid%20offline_access");
        var authorizeBody = await authorize.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.Found == authorize.StatusCode, $"{(int)authorize.StatusCode}: {authorizeBody} (client={clientId})");
        var interaction = System.Web.HttpUtility.ParseQueryString(authorize.Headers.Location!.Query);
        var approve = await client.PostAsync("/connect/interact",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["code"]      = interaction[Parameters.Code]!,
                ["code_type"] = interaction["code_type"]!,
            }));
        var approveBody = await approve.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.Found == approve.StatusCode, $"{(int)approve.StatusCode}: {approveBody}");
        var callback = System.Web.HttpUtility.ParseQueryString(approve.Headers.Location!.Query);
        var code     = callback[Parameters.Code];
        Assert.False(string.IsNullOrWhiteSpace(code));

        // Token leg: client_id alone, no client_secret anywhere on the wire.
        var token = await client.SendAsync(Token([
            new("grant_type", GrantTypes.AuthorizationCode),
            new("client_id", clientId),
            new("code", code),
            new("redirect_uri", "https://localhost/callback"),
            new("code_verifier", Verifier),
        ]));
        var tokenBody = await token.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.OK == token.StatusCode, $"{(int)token.StatusCode}: {tokenBody}");

        var pair         = JsonDocument.Parse(tokenBody).RootElement;
        var refreshToken = pair.GetProperty("refresh_token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(refreshToken));

        // Refresh leg: still client_id alone.
        var refreshed = await client.SendAsync(Token([
            new("grant_type", GrantTypes.RefreshToken),
            new("client_id", clientId),
            new("refresh_token", refreshToken),
        ]));
        var refreshBody = await refreshed.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.OK == refreshed.StatusCode, $"{(int)refreshed.StatusCode}: {refreshBody}");
        Assert.False(string.IsNullOrWhiteSpace(
            JsonDocument.Parse(refreshBody).RootElement.GetProperty("access_token").GetString()));
    }

    [Fact]
    public async Task None_Client_Is_Not_A_Client_Credentials_Authority() {
        using var flow = factory.WithEnvironment("Testing");
        using var client = flow.CreateClient();
        var clientId = await RegisterNoneClientAsync(client);

        var response = await client.SendAsync(Token([
            new("grant_type", GrantTypes.ClientCredentials),
            new("client_id", clientId),
        ]));
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(OAuthErrors.InvalidClient, body.GetProperty("error").GetString());
    }

    private static async Task<string> RegisterNoneClientAsync(HttpClient client) {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/register");
        request.Content = JsonContent.Create(new Dictionary<string, object?> {
            ["redirect_uris"]              = new[] { "https://localhost/callback" },
            ["token_endpoint_auth_method"] = ClientAuthMethods.None,
            ["grant_types"]                = new[] { GrantTypes.AuthorizationCode, GrantTypes.RefreshToken },
            ["response_types"]             = new[] { "code" },
            ["scope"]                      = "openid offline_access",
        });
        request.Headers.Authorization = new("Bearer", "initial-token");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.Created == response.StatusCode, $"{(int)response.StatusCode}: {body}");

        var root     = JsonDocument.Parse(body).RootElement;
        var clientId = root.GetProperty("client_id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(clientId));

        // Read-back through RFC 7592: proves the client persisted in this host's storage.
        var rat = root.GetProperty("registration_access_token").GetString();
        using var read = new HttpRequestMessage(HttpMethod.Get, $"/connect/register/{clientId}");
        read.Headers.Authorization = new("Bearer", rat);
        using var readResponse = await client.SendAsync(read);
        var readBody = await readResponse.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.OK == readResponse.StatusCode, $"{(int)readResponse.StatusCode}: {readBody}");
        return clientId;
    }

    private static HttpRequestMessage Token(IEnumerable<KeyValuePair<string, string>> form) {
        return new(HttpMethod.Post, "/connect/token") {
            Content = new FormUrlEncodedContent(form),
        };
    }

    private static void Configure_Interaction_Authentication(IServiceCollection services) {
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
        public const string SchemeName = "DcrNoneTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
            if (Request.Headers.Authorization != SchemeName) {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new System.Security.Claims.ClaimsIdentity(
                [new("sub", "users/u-1")], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new(new(identity), SchemeName)));
        }
    }
}
