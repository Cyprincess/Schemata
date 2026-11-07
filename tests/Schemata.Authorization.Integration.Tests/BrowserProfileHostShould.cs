using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Component")]
public class BrowserProfileHostShould : IDisposable
{
    private const string Origin = "https://127.0.0.1:4173";
    private const string Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";
    private readonly RSA _key = RSA.Create(2048);

    [Fact]
    public async Task Advertise_The_Registered_Modern_Profile_And_Exact_Origin() {
        using var factory = new WebAppFactory().WithEnvironment("BrowserProfile");
        using var client = factory.CreateClient();

        using var discovery = await JsonDocument.ParseAsync(
            await client.GetStreamAsync("/.well-known/openid-configuration"));
        Assert.Contains(GrantTypes.AuthorizationCode, Values(discovery.RootElement, "grant_types_supported"));
        Assert.Contains(GrantTypes.RefreshToken, Values(discovery.RootElement, "grant_types_supported"));
        Assert.Contains(ResponseModes.FormPost, Values(discovery.RootElement, "response_modes_supported"));
        Assert.Equal("S256", Assert.Single(discovery.RootElement.GetProperty("code_challenge_methods_supported").EnumerateArray()).GetString());
        Assert.Equal("https://localhost/Connect/EndSession", discovery.RootElement.GetProperty("end_session_endpoint").GetString());

        using var allowedRequest = Preflight(Origin);
        using var allowed = await client.SendAsync(allowedRequest);
        Assert.Equal(Origin, allowed.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("GET,POST,OPTIONS", Assert.Single(allowed.Headers.GetValues("Access-Control-Allow-Methods")));

        using var deniedRequest = Preflight("https://denied.example");
        using var denied = await client.SendAsync(deniedRequest);
        Assert.False(denied.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Reject_A_Public_Code_Request_Without_Pkce() {
        using var factory = new WebAppFactory().WithEnvironment("BrowserProfile");
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new(WebAppFactory.InteractionScheme);

        using var response = await Authorize(client, AuthorizeUrl("query", pkce: false));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var callback = HttpUtility.ParseQueryString(response.Headers.Location!.Query);
        Assert.Equal(OAuthErrors.InvalidRequest, callback[Parameters.Error]);
        Assert.Equal("browser-state", callback[Parameters.State]);
        Assert.Null(callback[Parameters.Code]);
        Assert.Null(callback[Parameters.AccessToken]);
    }

    [Theory]
    [InlineData("query")]
    [InlineData("form_post")]
    public async Task Return_Only_Code_And_State_In_The_Authorization_Response(string mode) {
        using var factory = new WebAppFactory().WithEnvironment("BrowserProfile");
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new(WebAppFactory.InteractionScheme);

        using var response = await Authorize(client, AuthorizeUrl(mode, pkce: true));

        if (mode == "query") {
            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            var callback = HttpUtility.ParseQueryString(response.Headers.Location!.Query);
            Assert.False(string.IsNullOrWhiteSpace(callback[Parameters.Code]));
            Assert.Equal("browser-state", callback[Parameters.State]);
            Assert.Null(callback[Parameters.AccessToken]);
            Assert.Null(callback[Parameters.IdToken]);
            return;
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("name=\"code\"", body);
        Assert.Contains("browser-state", body);
        Assert.DoesNotContain("access_token", body);
        Assert.DoesNotContain("id_token", body);
    }

    [Fact]
    public async Task Expose_The_Dpop_Nonce_And_Accept_The_Retried_Proof() {
        using var factory = new WebAppFactory().WithEnvironment("BrowserProfile");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", Origin);

        using var first = TokenRequest(Proof(null));
        using var challenge = await client.SendAsync(first);
        Assert.Equal(HttpStatusCode.BadRequest, challenge.StatusCode);
        var nonce = challenge.Headers.GetValues(Headers.DpopNonce).Single();
        Assert.Equal(Origin, challenge.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.True(challenge.Headers.TryGetValues("Access-Control-Expose-Headers", out var exposed),
            string.Join(" | ", challenge.Headers.Select(header => header.Key + "=" + string.Join(",", header.Value))));
        Assert.Contains(Headers.DpopNonce, string.Join(",", exposed));
        using var error = await JsonDocument.ParseAsync(await challenge.Content.ReadAsStreamAsync());
        Assert.Equal(OAuthErrors.UseDpopNonce, error.RootElement.GetProperty("error").GetString());

        using var retry = TokenRequest(Proof(nonce));
        using var response = await client.SendAsync(retry);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var tokens = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal(Schemes.Dpop, tokens.RootElement.GetProperty("token_type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(tokens.RootElement.GetProperty("access_token").GetString()));
    }

    private static HttpRequestMessage Preflight(string origin) {
        var request = new HttpRequestMessage(HttpMethod.Options, "/connect/token");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type,dpop");
        return request;
    }

    private static async Task<HttpResponseMessage> Authorize(HttpClient client, string url) {
        var initial = await client.GetAsync(url);
        if (initial.StatusCode != HttpStatusCode.Found
         || initial.Headers.Location is not { AbsolutePath: "/interact" } location) {
            return initial;
        }

        using (initial) {
            var interaction = HttpUtility.ParseQueryString(location.Query);
            return await client.PostAsync("/connect/interact", new FormUrlEncodedContent(new Dictionary<string, string> {
                ["code"] = interaction[Parameters.Code]!,
                ["code_type"] = interaction["code_type"]!,
            }));
        }
    }

    private static string AuthorizeUrl(string mode, bool pkce) {
        var url = "/connect/authorize?client_id=browser-client"
                + "&redirect_uri=" + Uri.EscapeDataString("https://127.0.0.1:4173/callback")
                + "&response_type=code&response_mode=" + mode
                + "&scope=openid%20offline_access&state=browser-state&nonce=browser-nonce";
        if (pkce) {
            url += "&code_challenge=" + Challenge + "&code_challenge_method=S256";
        }

        return url;
    }

    private HttpRequestMessage TokenRequest(string? proof) {
        var request = new HttpRequestMessage(HttpMethod.Post, "/connect/token") {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                ["grant_type"] = GrantTypes.ClientCredentials,
                ["client_id"] = "test-client",
                ["client_secret"] = "test-secret",
            }),
        };
        if (proof is not null) {
            request.Headers.Add(Headers.Dpop, proof);
        }

        return request;
    }

    private string Proof(string? nonce) {
        var parameters = _key.ExportParameters(false);
        var jwk = new Dictionary<string, object> {
            ["kty"] = "RSA",
            ["n"] = Base64UrlEncoder.Encode(parameters.Modulus!),
            ["e"] = Base64UrlEncoder.Encode(parameters.Exponent!),
        };
        var claims = new Dictionary<string, object> {
            ["jti"] = Guid.NewGuid().ToString(),
            ["htm"] = "POST",
            ["htu"] = "https://localhost/Connect/Token",
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        if (nonce is not null) {
            claims["nonce"] = nonce;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor {
            TokenType = TokenMediaTypes.DpopJwt,
            Claims = claims,
            SigningCredentials = new(new RsaSecurityKey(_key), SecurityAlgorithms.RsaSha256),
            AdditionalHeaderClaims = new Dictionary<string, object> { ["jwk"] = jwk },
        });
    }

    private static IEnumerable<string> Values(JsonElement document, string property) {
        return document.GetProperty(property).EnumerateArray().Select(value => value.GetString()!);
    }

    public void Dispose() {
        _key.Dispose();
    }
}
