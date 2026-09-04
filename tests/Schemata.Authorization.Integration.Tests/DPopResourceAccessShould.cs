using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
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
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Security.Skeleton.Services;
using Schemata.Caching.Skeleton;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Component")]
public class DPopResourceAccessShould
{
    private const string Htu = "http://localhost" + Endpoints.Profile;
    private static readonly DateTimeOffset Anchor = new(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);

    private readonly WebAppFactory _factory =
        new WebAppFactory().WithEnvironment("Dpop")
                           .WithServices(services => services.AddSingleton<DPopProofValidator>(
                               services => new(
                                   services.GetRequiredService<ICacheProvider>(),
                                   services.GetRequiredKeyedService<ITokenStore<SchemataToken>>(SecurityConstants.TokenTypes.Nonce),
                                   services.GetRequiredService<IOptions<DPopOptions>>(),
                                   new FakeTimeProvider(Anchor))));

    [Fact]
    public async Task Challenge_A_Proof_Less_Request_And_Bind_On_The_Nonce_Round_Trip() {
        using var key = RSA.Create(2048);

        var (_, jkt) = Proof(key);
        var token    = await MintAsync(jkt);
        var proof    = Proof(key, token).Proof;

        var client = _factory.CreateClient();
        var first  = await client.SendAsync(Profile(token, proof));

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);

        var dpop = Assert.Single(first.Headers.WwwAuthenticate, c => IsDpop(c));
        Assert.Contains($"error=\"{OAuthErrors.UseDpopNonce}\"", dpop.Parameter);
        Assert.Contains("algs=", dpop.Parameter);
        AssertHasBareBearer(first.Headers.WwwAuthenticate);

        var nonce = first.Headers.GetValues(Headers.DpopNonce).Single();

        var retry = Proof(key, token, nonce).Proof;
        var second = await client.SendAsync(Profile(token, retry));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var profile = JsonDocument.Parse(await second.Content.ReadAsStreamAsync()).RootElement;
        Assert.Equal(await AppNameAsync(), profile.GetProperty(IdentityClaims.Subject).GetString());
    }
    [Fact]
    public async Task Reject_A_Proof_Minted_For_A_Different_Token() {
        using var key = RSA.Create(2048);

        var jkt = Proof(key).Jkt;
        var token = await MintAsync(jkt);
        var nonce = await NonceAsync();
        var proof = Proof(key, "another-token-value", nonce).Proof;

        var response = await _factory.CreateClient().SendAsync(Profile(token, proof));

        await AssertRejectedAsync(response, OAuthErrors.InvalidDpopProof);
    }
    [Fact]
    public async Task Reject_A_Proof_From_A_Key_Other_Than_The_Bound_Key() {
        using var bound = RSA.Create(2048);
        using var other = RSA.Create(2048);

        var jkt = Proof(bound).Jkt;
        var token = await MintAsync(jkt);
        var nonce = await NonceAsync();
        var proof = Proof(other, token, nonce).Proof;

        var response = await _factory.CreateClient().SendAsync(Profile(token, proof));

        await AssertRejectedAsync(response, OAuthErrors.InvalidToken);
    }
    [Fact]
    public async Task Reject_A_Dpop_Bound_Token_Presented_As_Bearer() {
        using var key = RSA.Create(2048);

        var jkt = Proof(key).Jkt;
        var token = await MintAsync(jkt);

        var response = await _factory.CreateClient().SendAsync(Profile(token, null, Schemes.Bearer));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // RFC 9449 §7.2 Figure 18: the error rides the Bearer challenge the client used;
        // the DPoP companion challenge advertises algs without an error.
        var bearer = Assert.Single(response.Headers.WwwAuthenticate, c => IsBearer(c));
        Assert.Contains($"error=\"{OAuthErrors.InvalidToken}\"", bearer.Parameter);
        Assert.Contains("error_description=", bearer.Parameter);

        var dpop = Assert.Single(response.Headers.WwwAuthenticate, c => IsDpop(c));
        Assert.Contains("algs=", dpop.Parameter);
        Assert.DoesNotContain("error=", dpop.Parameter);
    }
    [Fact]
    public async Task Accept_An_Unbound_Token_Presented_Via_The_Dpop_Scheme() {
        using var key = RSA.Create(2048);

        var token = await MintAsync(null);
        var nonce = await NonceAsync();
        var proof = Proof(key, token, nonce).Proof;

        var response = await _factory.CreateClient().SendAsync(Profile(token, proof));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var profile = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        Assert.Equal(await AppNameAsync(), profile.GetProperty(IdentityClaims.Subject).GetString());
    }
    [Fact]
    public async Task Challenge_A_Request_Without_Credentials_With_Both_Schemes() {
        var response = await _factory.CreateClient().SendAsync(new(HttpMethod.Get, Endpoints.Profile));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // RFC 9449 §7.2 Figure 17: no credentials, no error on either challenge.
        Assert.Single(response.Headers.WwwAuthenticate, c => IsBearer(c) && c.Parameter is null);
        Assert.Single(response.Headers.WwwAuthenticate, c => IsDpop(c) && c.Parameter!.Contains("algs=") && !c.Parameter.Contains("error="));
    }
    [Fact]
    public async Task Authenticate_Dpop_Requests_Under_The_Dpop_Scheme() {
        using var key = RSA.Create(2048);

        var token = await MintAsync(null);
        var nonce = await NonceAsync();
        var proof = Proof(key, token, nonce, "http://localhost/test/whoami").Proof;

        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/test/whoami") {
            Headers = { Authorization = new(Schemes.Dpop, token) },
        };
        request.Headers.Add(Headers.Dpop, proof);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Schemes.Dpop, await response.Content.ReadAsStringAsync());
    }

    private static bool IsDpop(AuthenticationHeaderValue challenge) {
        return string.Equals(challenge.Scheme, Schemes.Dpop, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBearer(AuthenticationHeaderValue challenge) {
        return string.Equals(challenge.Scheme, Schemes.Bearer, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertHasBareBearer(HttpHeaderValueCollection<AuthenticationHeaderValue> challenges) {
        Assert.Single(challenges, c => IsBearer(c) && (c.Parameter is null || !c.Parameter.Contains("error=")));
    }

    private static async Task AssertRejectedAsync(HttpResponseMessage response, string error) {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var dpop = Assert.Single(response.Headers.WwwAuthenticate, c => IsDpop(c));
        Assert.Contains($"error=\"{error}\"", dpop.Parameter);

        AssertHasBareBearer(response.Headers.WwwAuthenticate);
    }

    private static HttpRequestMessage Profile(string token, string? proof, string scheme = Schemes.Dpop) {
        var request = new HttpRequestMessage(HttpMethod.Get, Endpoints.Profile) {
            Headers = { Authorization = new(scheme, token) },
        };

        if (proof is not null) {
            request.Headers.Add(Headers.Dpop, proof);
        }

        return request;
    }

    private async Task<string> AppNameAsync() {
        using var scope = _factory.Services.CreateScope();
        var apps = scope.ServiceProvider.GetRequiredService<IApplicationManager<SchemataApplication>>();

        return (await apps.FindByClientIdAsync("test-client"))!.CanonicalName!;
    }

    private async Task<string> MintAsync(string? jkt) {
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var app      = await AppNameAsync();

        var claims = new List<Claim> {
            new(IdentityClaims.Subject, app),
            new(Claims.Audience, app),
            new(Claims.Scope, Scopes.OpenId + " " + Scopes.Profile),
            new(Claims.ClientId, "test-client"),
        };

        if (jkt is not null) {
            claims.Add(new(Claims.Cnf, $"{{\"jkt\":\"{jkt}\"}}", JsonClaimValueTypes.Json));
        }

        return await SchemataAuthenticationHandler<SchemataApplication>.CreateTokenAsync(
            services.GetRequiredService<ITokenStore<SchemataToken>>(),
            services.GetRequiredService<TokenService>(),
            claims,
            TokenFormats.Jwt,
            TimeSpan.FromHours(1),
            TokenTypes.AccessToken,
            app,
            app,
            null,
            null,
            TimeProvider.System,
            default);
    }

    private async Task<string> NonceAsync() {
        using var scope    = _factory.Services.CreateScope();
        var       services = scope.ServiceProvider;
        var       nonces   = services.GetRequiredKeyedService<ITokenStore<SchemataToken>>(SecurityConstants.TokenTypes.Nonce);
        var       ttl      = services.GetRequiredService<IOptions<DPopOptions>>().Value.NonceLifetime;

        return (await nonces.GetOrCreateAsync(null, "dpop-rs", await AppNameAsync(), null, ttl, default)).Value!;
    }

    private static (string Proof, string Jkt) Proof(RSA key, string? token = null, string? nonce = null, string htu = Htu) {
        var parameters = key.ExportParameters(false);
        var jwk = new Dictionary<string, object> {
            ["kty"] = "RSA",
            ["n"]   = Base64UrlEncoder.Encode(parameters.Modulus!),
            ["e"]   = Base64UrlEncoder.Encode(parameters.Exponent!),
        };

        var claims = new Dictionary<string, object> {
            ["jti"] = Guid.NewGuid().ToString(),
            ["htm"] = "GET",
            ["htu"] = htu,
            ["iat"] = Anchor.ToUnixTimeSeconds(),
        };
        if (token is not null) {
            claims["ath"] = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(token)));
        }
        if (nonce is not null) {
            claims["nonce"] = nonce;
        }

        var proof = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor {
            TokenType              = TokenMediaTypes.DpopJwt,
            Claims                 = claims,
            SigningCredentials     = new(new RsaSecurityKey(key), "RS256"),
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
}
