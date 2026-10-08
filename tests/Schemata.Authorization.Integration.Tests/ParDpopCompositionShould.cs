using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Caching.Skeleton;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Component")]
public class ParDpopCompositionShould : IDisposable
{
    private readonly List<RSA> _keys = [];

    private const string Issuer    = "https://localhost";
    private const string ParUri    = Issuer + Endpoints.Par;
    private const string ClientId  = "code-client";
    private const string Secret    = "code-secret";
    private const string Redirect  = "https://localhost/callback";

    private static readonly DateTimeOffset Anchor = new(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);

    private readonly WebAppFactory _factory = new WebAppFactory()
        .WithEnvironment("Dpop")
        .WithServices(Pin_Proof_Clock);

    [Fact]
    public async Task Compose_The_Par_Proof_Header_Into_The_Stored_Request_And_Redeem_Request_Uri() {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (proof, jkt) = Proof();

        using var form = new FormUrlEncodedContent(new Dictionary<string, string> {
            [Parameters.ClientId]               = ClientId,
            [Parameters.ClientSecret]           = Secret,
            [Parameters.ResponseType]           = ResponseTypes.Code,
            [Parameters.RedirectUri]            = Redirect,
            [Parameters.Scope]                  = Scopes.OpenId,
            [Parameters.CodeChallenge]          = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            [Parameters.CodeChallengeMethod]    = "S256",
        });

        using var pushed = new HttpRequestMessage(HttpMethod.Post, Endpoints.Par) { Content = form };
        pushed.Headers.Add(Headers.Dpop, proof);
        using var response = await client.SendAsync(pushed);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created,
            $"{(int)response.StatusCode}: {body}");

        using var payload = JsonDocument.Parse(body);
        var requestUri = payload.RootElement.GetProperty("request_uri").GetString();
        Assert.NotNull(requestUri);
        Assert.StartsWith(RequestUriPrefixes.Par, requestUri);

        using (var scope = _factory.Services.CreateScope()) {
            var tokens = scope.ServiceProvider.GetRequiredService<ITokenStore<SchemataToken>>();
            var stored = await tokens.FindByReferenceIdAsync(requestUri[RequestUriPrefixes.Par.Length..]);
            Assert.NotNull(stored);
            Assert.Equal(TokenTypes.ParRequest, stored.Type);

            var options = scope.ServiceProvider.GetRequiredService<IOptions<JsonSerializerOptions>>().Value;
            var parsed = JsonSerializer.Deserialize<AuthorizeRequest>(stored.Payload!, options);
            Assert.NotNull(parsed);
            Assert.Equal(ClientId, parsed.ClientId);
            Assert.Equal(jkt, parsed.DpopJkt);
        }

        using var authorize = await client.GetAsync(Endpoints.Authorize
            + "?client_id=" + ClientId
            + "&request_uri=" + Uri.EscapeDataString(requestUri));

        Assert.Equal(HttpStatusCode.Found, authorize.StatusCode);
        var location = authorize.Headers.Location!;
        Assert.Equal("https://localhost/interact", location.GetLeftPart(UriPartial.Path));
        var query = HttpUtility.ParseQueryString(location.Query);
        Assert.False(string.IsNullOrWhiteSpace(query["code"]));
        Assert.False(string.IsNullOrWhiteSpace(query["code_type"]));
    }
    [Fact]
    public async Task Reject_A_Par_Request_Carrying_Two_Dpop_Header_Values() {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var (proof, _) = Proof();

        using var form = new FormUrlEncodedContent(new Dictionary<string, string> {
            [Parameters.ClientId]            = ClientId,
            [Parameters.ClientSecret]        = Secret,
            [Parameters.ResponseType]        = ResponseTypes.Code,
            [Parameters.RedirectUri]         = Redirect,
            [Parameters.Scope]               = Scopes.OpenId,
            [Parameters.CodeChallenge]       = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            [Parameters.CodeChallengeMethod] = "S256",
        });

        // RFC 9449 §4.3 step 1: a second field value makes the header ambiguous.
        using var pushed = new HttpRequestMessage(HttpMethod.Post, Endpoints.Par) { Content = form };
        Assert.True(pushed.Headers.TryAddWithoutValidation(Headers.Dpop, new[] { proof, "garbage" }));
        using var response = await client.SendAsync(pushed);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(OAuthErrors.InvalidDpopProof, error.GetProperty("error").GetString());
    }

    /// <summary>Pins only the proof validator's clock, so minted iat values stay valid while the rest of the host keeps the system clock.</summary>
    private static void Pin_Proof_Clock(IServiceCollection services) {
        services.AddScoped<DPopProofValidator>(services => new(
            services.GetRequiredService<ICacheProvider>(),
            services.GetRequiredKeyedService<ITokenStore<SchemataToken>>(SecurityConstants.TokenTypes.Nonce),
            services.GetRequiredService<IOptions<DPopOptions>>(),
            new FakeTimeProvider(Anchor)));
    }

    /// <summary>Mints a DPoP proof for <c>POST {issuer}/Connect/Par</c> with no nonce — RFC 9449 §10.1 PAR composition does not require the nonce step.</summary>
    private (string Proof, string Jkt) Proof() {
        var rsa = RSA.Create(2048);
        _keys.Add(rsa);
        var parameters = rsa.ExportParameters(false);
        var jwk = new Dictionary<string, object> {
            ["kty"] = "RSA",
            ["n"]   = Base64UrlEncoder.Encode(parameters.Modulus!),
            ["e"]   = Base64UrlEncoder.Encode(parameters.Exponent!),
        };

        var claims = new Dictionary<string, object> {
            ["jti"] = Guid.NewGuid().ToString(),
            ["htm"] = "POST",
            ["htu"] = ParUri,
            ["iat"] = Anchor.ToUnixTimeSeconds(),
        };

        var proof = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor {
            TokenType              = TokenMediaTypes.DpopJwt,
            Claims                 = claims,
            SigningCredentials     = new(new RsaSecurityKey(rsa), "RS256"),
            AdditionalHeaderClaims = new Dictionary<string, object> { ["jwk"] = jwk },
        });

        var canonical = "{"
                      + string.Join(
                          ",",
                          jwk.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                             .Select(pair => $"\"{pair.Key}\":\"{pair.Value}\""))
                      + "}";
        var jkt = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

        return (proof, jkt);
    }

    public void Dispose() {
        _factory.Dispose();
        foreach (var key in _keys) {
            key.Dispose();
        }
    }
}