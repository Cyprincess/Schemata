using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Schemata.Caching.Skeleton;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Component")]
public class DPopFlowFeatureShould : IDisposable
{
    private readonly List<RSA> _keys = [];
    private const string Htu    = "https://localhost" + Endpoints.Token;
    private const string Issuer = "https://localhost";
    private static readonly DateTimeOffset Anchor = new(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);

    private readonly WebAppFactory _factory = new WebAppFactory()
        .WithEnvironment("Dpop")
        .WithServices(services => {
            services.Configure<SchemataAuthorizationOptions>(options => {
                options.AccessTokenFormat = TokenFormats.Jwt;
            });
            services.Configure<DPopOptions>(options => options.SigningAlgorithms.Remove(SigningAlgorithms.RsaSha512));
            Pin_Proof_Clock(services);
        });

    private readonly WebAppFactory _offFactory = new();

    [Fact]
    public async Task Advertise_The_Configured_Dpop_Signing_Algorithms_At_Discovery() {
        var client   = _factory.CreateClient();
        var response = await client.GetAsync(Issuer + "/.well-known/openid-configuration");
        var root     = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;

        var algorithms = root.GetProperty("dpop_signing_alg_values_supported");
        Assert.Contains(algorithms.EnumerateArray(), value => value.GetString() == "RS256");
        Assert.DoesNotContain(algorithms.EnumerateArray(), value => value.GetString() == "RS512");
    }
    [Fact]
    public async Task Omit_The_Dpop_Signing_Algorithms_When_The_Feature_Is_Off() {
        var client   = _offFactory.CreateClient();
        var response = await client.GetAsync(Issuer + "/.well-known/openid-configuration");
        var root     = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;

        Assert.False(root.TryGetProperty("dpop_signing_alg_values_supported", out var _));
    }
    [Fact]
    public async Task Reject_A_Proof_Less_Request_From_An_Unbound_Client_Under_The_Override() {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(Form("test-client", "test-secret", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        Assert.Equal(OAuthErrors.InvalidRequest, error.GetProperty("error").GetString());
    }
    [Fact]
    public async Task Bind_The_Token_When_A_Proof_Accompanies_The_Request_Under_The_Override() {
        var client       = _factory.CreateClient();
        var nonce        = await Warm_Nonce(client);
        var (proof, jkt) = Proof(nonce);

        var response = await client.SendAsync(Form("test-client", "test-secret", proof));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var token = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        Assert.Equal(Schemes.Dpop, token.GetProperty("token_type").GetString());
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token.GetProperty("access_token").GetString());
        Assert.True(jwt.TryGetPayloadValue<JsonElement>(Claims.Cnf, out var cnf));
        Assert.Equal(jkt, cnf.GetProperty(Claims.Jkt).GetString());
    }
    [Fact]
    public async Task Ignore_The_Dpop_Header_When_The_Feature_Is_Off() {
        var client     = _offFactory.CreateClient();
        var (proof, _) = Proof(null);

        var response = await client.SendAsync(Form("test-client", "test-secret", proof));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var token = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        Assert.Equal(Schemes.Bearer, token.GetProperty("token_type").GetString());
        Assert.Empty(response.Headers.WwwAuthenticate);
    }

    /// <summary>Triggers the §8 nonce challenge to learn the current server nonce value.</summary>
    private async Task<string> Warm_Nonce(HttpClient client) {
        var (proof, _) = Proof(null);

        var response = await client.SendAsync(Form("test-client", "test-secret", proof));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return response.Headers.GetValues(Headers.DpopNonce).Single();
    }
    /// <summary>Pins only the proof validator's clock, so minted iat values stay valid while the rest of the host keeps the system clock.</summary>
    private static void Pin_Proof_Clock(IServiceCollection services) {
        services.AddSingleton<DPopProofValidator>(services => new(
            services.GetRequiredService<ICacheProvider>(),
            services.GetRequiredKeyedService<ITokenStore<SchemataToken>>(SecurityConstants.TokenTypes.Nonce),
            services.GetRequiredService<IOptions<DPopOptions>>(),
            new FakeTimeProvider(Anchor)));
    }

    private (string Proof, string Jkt) Proof(string? nonce) {
        var rsa        = RSA.Create(2048);
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
            ["htu"] = Htu,
            ["iat"] = Anchor.ToUnixTimeSeconds(),
        };
        if (nonce is not null) {
            claims["nonce"] = nonce;
        }

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

    private static HttpRequestMessage Form(string clientId, string clientSecret, string? proof) {
        return Post(new() {
            ["grant_type"]    = GrantTypes.ClientCredentials,
            ["client_id"]     = clientId,
            ["client_secret"] = clientSecret,
        }, proof);
    }

    private static HttpRequestMessage Post(Dictionary<string, string> fields, string? proof) {
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoints.Token) {
            Content = new FormUrlEncodedContent(fields),
        };
        if (proof is not null) {
            request.Headers.Add(Headers.Dpop, proof);
        }

        return request;
    }

    public void Dispose() {
        foreach (var key in _keys) {
            key.Dispose();
        }
    }
}
