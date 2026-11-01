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
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Services;
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
                           .WithServices(services => services.AddScoped<DPopProofValidator<SchemataToken>>(
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
    public async Task Reject_A_Request_Carrying_Two_Dpop_Header_Values() {
        using var key = RSA.Create(2048);

        // The proof is valid and nonce-bearing; alone it authorizes the request.
        var jkt   = Proof(key).Jkt;
        var token = await MintAsync(jkt);
        var nonce = await NonceAsync();
        var proof = Proof(key, token, nonce).Proof;

        // RFC 9449 §4.3 step 1: a second field value makes the header ambiguous, so the
        // proof validator never runs.
        using var request = Profile(token, null);
        Assert.True(request.Headers.TryAddWithoutValidation(Headers.Dpop, new[] { proof, "garbage" }));

        var response = await _factory.CreateClient().SendAsync(request);

        await AssertRejectedAsync(response, OAuthErrors.InvalidDpopProof);
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
        var bearer = Assert.Single(response.Headers.WwwAuthenticate, IsBearer);
        var issuer = _factory.Services.GetRequiredService<IOptions<SchemataAuthorizationOptions>>().Value.Issuer;
        var realm = NameValueHeaderValue.Parse(bearer.Parameter!);
        Assert.Equal("realm", realm.Name);
        Assert.Equal(issuer, realm.Value!.Trim('"'));
        Assert.DoesNotContain("error=", bearer.Parameter, StringComparison.Ordinal);
        var dpop = Assert.Single(response.Headers.WwwAuthenticate, IsDpop);
        Assert.Contains("algs=", dpop.Parameter, StringComparison.Ordinal);
        Assert.DoesNotContain("error=", dpop.Parameter, StringComparison.Ordinal);
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Authenticate_A_Mounted_Request_Only_When_The_Proof_Covers_The_Path_Base(bool includesBase) {
        using var key = RSA.Create(2048);
        var jkt = Proof(key).Jkt;
        var token = await MintAsync(jkt);
        var nonce = await NonceAsync();
        var path = Endpoints.Profile;
        var target = "http://localhost" + (includesBase ? "/tenant-a" : string.Empty) + path;
        var proof = Proof(key, token, nonce, target).Proof;
        using var scope = _factory.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Scheme = "http";
        http.Request.Host = new("localhost");
        http.Request.PathBase = "/tenant-a";
        http.Request.Path = path;
        http.Request.Method = "GET";
        http.Request.Headers.Authorization = $"{Schemes.Dpop} {token}";
        http.Request.Headers[Headers.Dpop] = proof;

        var result = await http.AuthenticateAsync(Schemes.Dpop);

        if (includesBase) {
            Assert.True(result.Succeeded);
            Assert.Equal(await AppNameAsync(), result.Principal!.FindFirstValue(IdentityClaims.Subject));
            Assert.Equal(Schemes.Dpop, result.Principal.Identity!.AuthenticationType);
        } else {
            Assert.False(result.Succeeded);
            Assert.Null(result.Principal);
            await http.ChallengeAsync(Schemes.Dpop);
            Assert.Equal(StatusCodes.Status401Unauthorized, http.Response.StatusCode);
            Assert.Contains($"error=\"{OAuthErrors.InvalidDpopProof}\"", http.Response.Headers.WWWAuthenticate.ToString());
        }
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
            // RFC 9068 §2.2/§3: the minted access token audience is the default resource
            // (the issuer here) — not the client's canonical reference.
            new(Claims.Audience, "https://localhost"),
            new(Claims.Scope, Scopes.OpenId + " " + Scopes.Profile),
            new(Claims.ClientId, "test-client"),
        };

        if (jkt is not null) {
            claims.Add(new(Claims.Cnf, $"{{\"jkt\":\"{jkt}\"}}", JsonClaimValueTypes.Json));
        }
        var issuedAt = DateTimeOffset.UtcNow;

        var issuer = services.GetRequiredService<TokenService<SchemataSecurity>>();
        await using var signing = await issuer.BeginSigningAsync();
        return await SchemataAuthenticationHandler<SchemataApplication, SchemataSecurity, SchemataToken>.CreateTokenAsync(
            services.GetRequiredService<ITokenStore<SchemataToken>>(),
            services.GetRequiredService<Schemata.Authorization.Skeleton.Managers.IApplicationManager<SchemataApplication>>(),
            issuer,
            signing,
            claims,
            TokenFormats.Jwt,
            issuedAt,
            issuedAt + TimeSpan.FromHours(1),
            TokenTypes.AccessToken,
            app,
            app,
            null,
            null,
            new() {
                Subject = app, SubjectKind = GrantSubjectKinds.EndUser,
                Profile = GrantProfiles.OpenIdConnect, Source = GrantTypes.AuthorizationCode,
                Scope = $"{Scopes.OpenId} {Scopes.Profile}",
            },
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
