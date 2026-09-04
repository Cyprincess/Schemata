using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using static Schemata.Abstractions.SchemataConstants;
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
public class DPopTokenEndpointShould : IDisposable
{
    private readonly List<RSA> _keys = [];
    private const string Htu         = "https://localhost" + Endpoints.Token;
    private const string RedirectUri = "https://localhost/callback";

    /// <summary>The RFC 7636 Appendix B verifier/challenge pair.</summary>
    private const string Verifier  = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
    private const string Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";
    private static readonly DateTimeOffset Anchor = new(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);

    private readonly WebAppFactory _factory = new WebAppFactory()
        .WithEnvironment("Dpop")
        .WithServices(services => {
            services.Configure<SchemataAuthorizationOptions>(options => options.AccessTokenFormat = TokenFormats.Jwt);
            Pin_Proof_Clock(services);
        });

    private readonly WebAppFactory _refreshFactory = new WebAppFactory()
        .WithEnvironment("Dpop")
        .WithServices(services => {
            services.Configure<SchemataAuthorizationOptions>(options => {
                options.AccessTokenFormat  = TokenFormats.Jwt;
                options.RefreshTokenFormat = TokenFormats.Jwt;
            });
            Pin_Proof_Clock(services);
        });

    [Fact]
    public async Task Challenge_A_Proof_Less_Nonce_And_Bind_The_Key_Across_The_Round_Trip() {
        var client = _factory.CreateClient();
        var first = await client.SendAsync(Form("test-client", "test-secret", Proof(null).Proof));

        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);

        var challenge = JsonDocument.Parse(await first.Content.ReadAsStreamAsync()).RootElement;
        Assert.Equal(OAuthErrors.UseDpopNonce, challenge.GetProperty("error").GetString());
        var nonce = first.Headers.GetValues(Headers.DpopNonce).Single();

        var (proof, jkt) = Proof(nonce);
        var second = await client.SendAsync(Form("test-client", "test-secret", proof));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var token = JsonDocument.Parse(await second.Content.ReadAsStreamAsync()).RootElement;
        Assert.Equal(Schemes.Dpop, token.GetProperty("token_type").GetString());
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token.GetProperty("access_token").GetString());
        Assert.True(jwt.TryGetPayloadValue<JsonElement>(Claims.Cnf, out var cnf));
        Assert.Equal(jkt, cnf.GetProperty(Claims.Jkt).GetString());
    }
    [Fact]
    public async Task Reject_A_Proof_Less_Request_From_A_Bound_Client() {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(Form("dpop-client", "dpop-secret", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        Assert.Equal(OAuthErrors.InvalidRequest, error.GetProperty("error").GetString());
    }
    [Fact]
    public async Task Bind_The_Access_Token_To_The_Key_Committed_At_Authorize() {
        var client       = _factory.CreateClient();
        var nonce        = await Warm_Nonce(client);
        var (proof, jkt) = Proof(nonce);
        var code         = await Mint_Code(_factory, jkt);

        var response = await client.SendAsync(Exchange(code, proof));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var token = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        Assert.Equal(Schemes.Dpop, token.GetProperty("token_type").GetString());
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token.GetProperty("access_token").GetString());
        Assert.True(jwt.TryGetPayloadValue<JsonElement>(Claims.Cnf, out var cnf));
        Assert.Equal(jkt, cnf.GetProperty(Claims.Jkt).GetString());
    }
    [Fact]
    public async Task Reject_An_Exchange_Proof_From_A_Different_Key() {
        var client     = _factory.CreateClient();
        var nonce      = await Warm_Nonce(client);
        var (_, jkt)   = Proof(nonce);
        var code       = await Mint_Code(_factory, jkt);
        var (other, _) = Proof(nonce);

        var response = await client.SendAsync(Exchange(code, other));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        Assert.Equal(OAuthErrors.InvalidGrant, error.GetProperty("error").GetString());
    }
    [Fact]
    public async Task Reject_An_Exchange_Without_The_Committed_Proof() {
        var client   = _factory.CreateClient();
        var nonce    = await Warm_Nonce(client);
        var (_, jkt) = Proof(nonce);
        var code     = await Mint_Code(_factory, jkt);

        var response = await client.SendAsync(Exchange(code, null));

        var error = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;

        // §5.2 host-wide enforcement answers the missing proof before the grant runs.
        Assert.Equal(OAuthErrors.InvalidRequest, error.GetProperty("error").GetString());
    }
    [Fact]
    public async Task Carry_The_Committed_Key_Binding_Into_The_Refresh_Token() {
        var client  = _refreshFactory.CreateClient();
        var pair    = await Mint_Bound_Pair(client);

        var refresh = new JsonWebTokenHandler().ReadJsonWebToken(pair.RefreshToken);

        Assert.True(refresh.TryGetPayloadValue<JsonElement>(Claims.Cnf, out var cnf));
        Assert.Equal(pair.Jkt, cnf.GetProperty(Claims.Jkt).GetString());
    }
    [Fact]
    public async Task Refresh_A_Bound_Token_With_A_Proof_From_The_Same_Key() {
        var client = _refreshFactory.CreateClient();
        var pair   = await Mint_Bound_Pair(client);

        var (proof, _) = Proof(pair.Key, pair.Jwk, pair.Nonce);
        var response   = await client.SendAsync(Refresh(pair.RefreshToken, proof));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var token = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        Assert.Equal(Schemes.Dpop, token.GetProperty("token_type").GetString());
    }
    [Fact]
    public async Task Reject_A_Refresh_Proof_From_A_Different_Key() {
        var client     = _refreshFactory.CreateClient();
        var pair       = await Mint_Bound_Pair(client);
        var (other, _) = Proof(pair.Nonce);

        var response = await client.SendAsync(Refresh(pair.RefreshToken, other));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        Assert.Equal(OAuthErrors.InvalidDpopProof, error.GetProperty("error").GetString());
    }
    [Fact]
    public async Task Reject_A_Proof_Less_Refresh_Of_A_Bound_Token() {
        var client = _refreshFactory.CreateClient();
        var pair   = await Mint_Bound_Pair(client);

        var response = await client.SendAsync(Refresh(pair.RefreshToken, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;

        // §5.2 host-wide enforcement answers the missing proof before the refresh grant runs.
        Assert.Equal(OAuthErrors.InvalidRequest, error.GetProperty("error").GetString());
    }
    [Fact]
    public async Task Pass_Through_As_Bearer_When_The_Authorize_Request_Commits_No_Key() {
        using var factory = _factory.WithEnvironment("Authenticated").WithServices(Configure_Interaction_Authentication);
        var       client  = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new(InteractionAuthenticationHandler.SchemeName);

        // Wire leg 1: /connect/authorize without dpop_jkt — the advisor passes through and the
        // pipeline issues an interaction reference.
        var authorize = await client.GetAsync("/connect/authorize?client_id=code-client"
            + "&redirect_uri=https%3A%2F%2Flocalhost%2Fcallback"
            + "&response_type=code"
            + "&code_challenge=" + Challenge
            + "&code_challenge_method=S256");
        Assert.Equal(HttpStatusCode.Found, authorize.StatusCode);

        var interaction = System.Web.HttpUtility.ParseQueryString(authorize.Headers.Location!.Query);
        var approve = await client.PostAsync(
            "/connect/interact",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["code"]      = interaction[Parameters.Code]!,
                ["code_type"] = interaction[Parameters.CodeType]!,
            }));
        var approveBody = await approve.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.Found == approve.StatusCode, approveBody);

        var callback = System.Web.HttpUtility.ParseQueryString(approve.Headers.Location!.Query);

        // Wire leg 2: the code exchange carries no DPoP proof — plain Bearer with no cnf member.
        var response = await client.SendAsync(Post(new() {
            ["grant_type"]    = GrantTypes.AuthorizationCode,
            ["client_id"]     = "code-client",
            ["client_secret"] = "code-secret",
            ["code"]          = callback[Parameters.Code]!,
            ["redirect_uri"]  = RedirectUri,
            ["code_verifier"] = Verifier,
        }, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var token = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        Assert.Equal(Schemes.Bearer, token.GetProperty("token_type").GetString());
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token.GetProperty("access_token").GetString());
        Assert.False(jwt.TryGetPayloadValue<JsonElement>(Claims.Cnf, out _));
    }

    /// <summary>
    ///     Mints a code whose payload carries the committed thumbprint, by issuing through the
    ///     sign-in service the same way the interaction approval path does.
    /// </summary>
    private static async Task<string> Mint_Code(WebAppFactory factory, string jkt) {
        using var scope  = factory.Services.CreateScope();
        var       signIn = scope.ServiceProvider.GetRequiredService<IAuthorizationSignInService>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new List<Claim> {
                new(IdentityClaims.Subject, "users/u-1"),
                new(Claims.ClientId, "code-client"),
            }, "test"));

        var response = await signIn.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType]    = GrantTypes.AuthorizationCode,
            [Properties.ResponseType] = ResponseTypes.Code,
            [Properties.Scope]        = "openid offline_access",
            [Properties.RedirectUri]  = RedirectUri,
            [Properties.DpopJkt]      = jkt,
        }, AuthorizationSignInResponseKind.Callback);

        return response.Callback!.Parameters[Parameters.Code]!;
    }

    private async Task<(string RefreshToken, string Jkt, string Nonce, RsaSecurityKey Key, Dictionary<string, object> Jwk)>
        Mint_Bound_Pair(HttpClient client) {
        var nonce        = await Warm_Nonce(client);
        var (key, jwk)   = Rsa_Key();
        var (proof, jkt) = Proof(key, jwk, nonce);
        var code         = await Mint_Code(_refreshFactory, jkt);

        var response = await client.SendAsync(Exchange(code, proof));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var token = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        return (
            token.GetProperty("refresh_token").GetString()!,
            jkt,
            nonce,
            key,
            jwk);
    }

    /// <summary>Triggers the §8 nonce challenge to learn the current server nonce value.</summary>
    private async Task<string> Warm_Nonce(HttpClient client) {
        var (proof, _) = Proof(null);

        var response = await client.SendAsync(Post(new() {
            ["grant_type"]    = GrantTypes.AuthorizationCode,
            ["client_id"]     = "code-client",
            ["client_secret"] = "code-secret",
            ["code"]          = "unused",
        }, proof));

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

    private static HttpRequestMessage Exchange(string code, string? proof) {
        return Post(new() {
            ["grant_type"]    = GrantTypes.AuthorizationCode,
            ["client_id"]     = "code-client",
            ["client_secret"] = "code-secret",
            ["code"]          = code,
            ["redirect_uri"]  = RedirectUri,
        }, proof);
    }

    private static HttpRequestMessage Refresh(string refreshToken, string? proof) {
        return Post(new() {
            ["grant_type"]    = GrantTypes.RefreshToken,
            ["client_id"]     = "code-client",
            ["client_secret"] = "code-secret",
            ["refresh_token"] = refreshToken,
        }, proof);
    }

    private static HttpRequestMessage Post(Dictionary<string, string> fields, string? proof) {
        var request = new HttpRequestMessage(HttpMethod.Post, "/connect/token") {
            Content = new FormUrlEncodedContent(fields),
        };
        if (proof is not null) {
            request.Headers.Add(Headers.Dpop, proof);
        }

        return request;
    }

    private static HttpRequestMessage Form(string clientId, string clientSecret, string? proof) {
        return Post(new() {
            ["grant_type"]    = GrantTypes.ClientCredentials,
            ["client_id"]     = clientId,
            ["client_secret"] = clientSecret,
        }, proof);
    }

    private (RsaSecurityKey Key, Dictionary<string, object> Jwk) Rsa_Key() {
        var rsa        = RSA.Create(2048);
        _keys.Add(rsa);
        var parameters = rsa.ExportParameters(false);
        var jwk = new Dictionary<string, object> {
            ["kty"] = "RSA",
            ["n"]   = Base64UrlEncoder.Encode(parameters.Modulus!),
            ["e"]   = Base64UrlEncoder.Encode(parameters.Exponent!),
        };
        return (new(rsa), jwk);
    }

    private (string Proof, string Jkt) Proof(string? nonce) {
        var (key, jwk) = Rsa_Key();
        return Proof(key, jwk, nonce);
    }

    private static (string Proof, string Jkt) Proof(
        RsaSecurityKey             key,
        Dictionary<string, object> jwk,
        string?                    nonce
    ) {
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
            SigningCredentials     = new(key, "RS256"),
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

    public void Dispose() {
        foreach (var key in _keys) {
            key.Dispose();
        }
    }
}
