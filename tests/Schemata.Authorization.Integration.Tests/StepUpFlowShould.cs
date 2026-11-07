using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Caching.Skeleton;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Component")]
public class StepUpFlowShould
{
    private const string RedirectUri = "https://localhost/callback";
    private const string SessionScheme = "ManagementTest";
    private const string Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";
    private const string Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
    private const string Password = "urn:example:password";
    private const string Multifactor = "urn:example:mfa";
    private const string Unsatisfiable = "urn:example:hardware";
    private const string Htu = "http://localhost" + Endpoints.Profile;
    private static readonly DateTimeOffset Anchor = new(2026, 11, 5, 0, 0, 0, TimeSpan.Zero);
    private static readonly long StaleAuthTime = Anchor.AddHours(-1).ToUnixTimeSeconds();
    private static readonly long FreshAuthTime = Anchor.ToUnixTimeSeconds();
    private static readonly ResourceAuthenticationRequirement Requirement = new([Multifactor], TimeSpan.FromSeconds(300));

    [Fact]
    public async Task Challenge_Insufficient_Bearer_Evidence_And_Accept_A_Satisfying_Token() {
        using var factory = NewFactory(Requirement);
        var insufficient = await MintResourceToken(factory, Password, StaleAuthTime);

        var rejected = await factory.CreateClient().SendAsync(ResourceRequest(insufficient));

        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        var challenge = Assert.Single(rejected.Headers.WwwAuthenticate, value => value.Scheme == Schemes.Bearer);
        Assert.Contains($"error=\"{OAuthErrors.InsufficientUserAuthentication}\"", challenge.Parameter, StringComparison.Ordinal);
        Assert.Contains($"acr_values=\"{Multifactor}\"", challenge.Parameter, StringComparison.Ordinal);
        Assert.Contains("max_age=\"300\"", challenge.Parameter, StringComparison.Ordinal);

        var satisfying = await MintResourceToken(factory, Multifactor, FreshAuthTime);
        var accepted = await factory.CreateClient().SendAsync(ResourceRequest(satisfying));

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var profile = JsonDocument.Parse(await accepted.Content.ReadAsStreamAsync()).RootElement;
        Assert.Equal(await AppName(factory), profile.GetProperty(IdentityClaims.Subject).GetString());
    }

    [Fact]
    public async Task Challenge_Insufficient_Dpop_Evidence_And_Accept_A_Satisfying_Token() {
        using var factory = NewFactory(Requirement, dpop: true);
        using var key = RSA.Create(2048);
        var jkt = Proof(key).Jkt;
        var insufficient = await MintResourceToken(factory, Password, StaleAuthTime, jkt);
        var insufficientNonce = await Nonce(factory);
        var insufficientProof = Proof(key, insufficient, insufficientNonce).Proof;

        var rejected = await factory.CreateClient().SendAsync(ResourceRequest(insufficient, insufficientProof, Schemes.Dpop));

        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        var challenge = Assert.Single(rejected.Headers.WwwAuthenticate, value => value.Scheme == Schemes.Dpop);
        Assert.Contains($"error=\"{OAuthErrors.InsufficientUserAuthentication}\"", challenge.Parameter, StringComparison.Ordinal);
        Assert.Contains($"acr_values=\"{Multifactor}\"", challenge.Parameter, StringComparison.Ordinal);
        Assert.Contains("max_age=\"300\"", challenge.Parameter, StringComparison.Ordinal);

        var satisfying = await MintResourceToken(factory, Multifactor, FreshAuthTime, jkt);
        var satisfyingNonce = await Nonce(factory);
        var satisfyingProof = Proof(key, satisfying, satisfyingNonce).Proof;
        var accepted = await factory.CreateClient().SendAsync(ResourceRequest(satisfying, satisfyingProof, Schemes.Dpop));

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Fact]
    public async Task Complete_Genuine_Step_Up_And_Preserve_The_Event_On_Refresh() {
        using var factory = NewFactory(Requirement);
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new(SessionScheme);

        var interaction = await BeginAuthorization(client, scope: $"{Scopes.OpenId} {Scopes.OfflineAccess}");
        var code = await Approve(client, interaction, stepUp: true);
        var issued = await Exchange(client, code);
        var access = issued.GetProperty("access_token").GetString();
        var refresh = issued.GetProperty("refresh_token").GetString();
        Assert.NotNull(access);
        Assert.NotNull(refresh);
        var evidence = Payload(access);
        Assert.Equal(Multifactor, evidence.GetProperty(Claims.Acr).GetString());
        Assert.Equal(FreshAuthTime, evidence.GetProperty(Claims.AuthTime).GetInt64());

        var resource = await factory.CreateClient().SendAsync(ResourceRequest(access));
        Assert.Equal(HttpStatusCode.OK, resource.StatusCode);

        var renewed = await Refresh(client, refresh);
        var renewedAccess = renewed.GetProperty("access_token").GetString();
        Assert.NotNull(renewedAccess);
        Assert.NotEqual(access, renewedAccess);
        var renewedEvidence = Payload(renewedAccess);
        Assert.Equal(Multifactor, renewedEvidence.GetProperty(Claims.Acr).GetString());
        Assert.Equal(FreshAuthTime, renewedEvidence.GetProperty(Claims.AuthTime).GetInt64());
    }

    [Fact]
    public async Task Return_Unmet_Requirements_When_A_Silent_Request_Cannot_Step_Up() {
        using var factory = NewFactory(Requirement);
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new(SessionScheme);
        var interaction = await BeginAuthorization(client);
        await Approve(client, interaction, stepUp: true);

        var response = await client.GetAsync(AuthorizeUrl(prompt: PromptValues.None, maxAge: 900));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var callback = QueryHelpers.ParseQuery(response.Headers.Location!.Query);
        Assert.Equal(OAuthErrors.UnmetAuthenticationRequirements, callback[Parameters.Error]);
        Assert.Equal("xyz", callback[Parameters.State]);
    }

    [Fact]
    public async Task Preserve_Ordinary_Silent_Max_Age_Error_Without_A_Resource_Requirement() {
        using var factory = NewFactory(null);
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new(SessionScheme);
        var interaction = await BeginAuthorization(client);
        await Approve(client, interaction, stepUp: false);

        var response = await client.GetAsync(AuthorizeUrl(prompt: PromptValues.None, maxAge: 900));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var callback = QueryHelpers.ParseQuery(response.Headers.Location!.Query);
        Assert.Equal(OAuthErrors.LoginRequired, callback[Parameters.Error]);
        Assert.Equal("xyz", callback[Parameters.State]);
    }

    [Fact]
    public async Task Reject_An_Approval_That_Did_Not_Satisfy_The_Requirement() {
        using var factory = NewFactory(Requirement);
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new(SessionScheme);
        var interaction = await BeginAuthorization(client);

        var response = await ApproveResponse(client, interaction, stepUp: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        Assert.Equal(OAuthErrors.UnmetAuthenticationRequirements, error.GetProperty(Parameters.Error).GetString());
    }

    [Fact]
    public async Task Preserve_Voluntary_Acr_When_No_Resource_Requirement_Applies() {
        using var factory = NewFactory(null);
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new(SessionScheme);
        var interaction = await BeginAuthorization(client, acrValues: Unsatisfiable);

        var code = await Approve(client, interaction, stepUp: false);
        var issued = await Exchange(client, code);

        var evidence = Payload(issued.GetProperty("access_token").GetString()!);
        Assert.Equal(Password, evidence.GetProperty(Claims.Acr).GetString());
        Assert.Equal(StaleAuthTime, evidence.GetProperty(Claims.AuthTime).GetInt64());
    }

    private static WebAppFactory NewFactory(ResourceAuthenticationRequirement? requirement, bool dpop = false) {
        return new WebAppFactory().WithEnvironment(dpop ? "Dpop" : "Authenticated").WithServices(services => {
            services.AddAuthentication(SessionScheme)
                    .AddScheme<AuthenticationSchemeOptions, StepUpSessionHandler>(SessionScheme, _ => { });
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(Anchor));
            services.AddSingleton<IResourceAuthenticationRequirementProvider>(new StaticRequirementProvider(requirement));
            services.PostConfigure<SchemataAuthorizationOptions>(options => options.AccessTokenFormat = TokenFormats.Jwt);
            if (dpop) {
                services.AddScoped<DPopProofValidator<SchemataToken>>(provider => new(
                    provider.GetRequiredService<ICacheProvider>(),
                    provider.GetRequiredKeyedService<ITokenStore<SchemataToken>>(SecurityConstants.TokenTypes.Nonce),
                    provider.GetRequiredService<IOptions<DPopOptions>>(),
                    new FakeTimeProvider(Anchor)));
            }
        });
    }

    private static string AuthorizeUrl(
        string? acrValues = null,
        string? prompt = null,
        string scope = Scopes.OpenId,
        long? maxAge = null
    ) {
        return "/connect/authorize?client_id=code-client"
             + "&redirect_uri=" + Uri.EscapeDataString(RedirectUri)
             + "&response_type=code&state=xyz"
             + "&scope=" + Uri.EscapeDataString(scope)
             + (acrValues is null ? string.Empty : "&acr_values=" + Uri.EscapeDataString(acrValues))
             + (prompt is null ? string.Empty : "&prompt=" + Uri.EscapeDataString(prompt))
             + (maxAge is null ? string.Empty : "&max_age=" + maxAge.Value)
             + "&code_challenge=" + Challenge
             + "&code_challenge_method=S256";
    }

    private static async Task<Dictionary<string, string?>> BeginAuthorization(
        HttpClient client,
        string? acrValues = null,
        string scope = Scopes.OpenId
    ) {
        var response = await client.GetAsync(AuthorizeUrl(acrValues, scope: scope));
        Assert.True(HttpStatusCode.Found == response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal("https://localhost/interact", response.Headers.Location!.GetLeftPart(UriPartial.Path));
        return QueryHelpers.ParseQuery(response.Headers.Location.Query)
                           .ToDictionary(pair => pair.Key, pair => (string?)pair.Value.ToString());
    }

    private static async Task<string> Approve(
        HttpClient client,
        IReadOnlyDictionary<string, string?> interaction,
        bool stepUp
    ) {
        using var response = await ApproveResponse(client, interaction, stepUp);
        Assert.True(HttpStatusCode.Found == response.StatusCode, await response.Content.ReadAsStringAsync());
        var callback = QueryHelpers.ParseQuery(response.Headers.Location!.Query);
        return callback[Parameters.Code].ToString();
    }

    private static Task<HttpResponseMessage> ApproveResponse(
        HttpClient client,
        IReadOnlyDictionary<string, string?> interaction,
        bool stepUp
    ) {
        var request = new HttpRequestMessage(HttpMethod.Post, "/connect/interact") {
            Content = new FormUrlEncodedContent(new Dictionary<string, string?> {
                ["code"] = interaction[Parameters.Code],
                ["code_type"] = interaction["code_type"],
            }),
        };
        if (stepUp) {
            request.Headers.Add("X-Step-Up", "true");
        }
        return client.SendAsync(request);
    }

    private static async Task<JsonElement> Exchange(HttpClient client, string code) {
        return await PostToken(client, [
            new("grant_type", GrantTypes.AuthorizationCode),
            new("client_id", "code-client"),
            new("client_secret", "code-secret"),
            new("code", code),
            new("redirect_uri", RedirectUri),
            new("code_verifier", Verifier),
        ]);
    }

    private static async Task<JsonElement> Refresh(HttpClient client, string refresh) {
        return await PostToken(client, [
            new("grant_type", GrantTypes.RefreshToken),
            new("client_id", "code-client"),
            new("client_secret", "code-secret"),
            new("refresh_token", refresh),
        ]);
    }

    private static async Task<JsonElement> PostToken(HttpClient client, IEnumerable<KeyValuePair<string, string>> form) {
        using var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(form));
        Assert.True(HttpStatusCode.OK == response.StatusCode, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement.Clone();
    }

    private static HttpRequestMessage ResourceRequest(string token, string? proof = null, string scheme = Schemes.Bearer) {
        var request = new HttpRequestMessage(HttpMethod.Get, Endpoints.Profile) {
            Headers = { Authorization = new(scheme, token) },
        };
        if (proof is not null) {
            request.Headers.Add(Headers.Dpop, proof);
        }
        return request;
    }

    private static async Task<string> MintResourceToken(
        WebAppFactory factory,
        string acr,
        long authTime,
        string? jkt = null
    ) {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var app = await AppName(factory);
        var claims = new List<Claim> {
            new(IdentityClaims.Subject, app),
            new(Claims.Audience, "https://localhost"),
            new(Claims.Scope, $"{Scopes.OpenId} {Scopes.Profile}"),
            new(Claims.ClientId, "test-client"),
            new(Claims.Acr, acr),
            new(Claims.AuthTime, authTime.ToString(System.Globalization.CultureInfo.InvariantCulture), ClaimValueTypes.Integer64),
        };
        if (jkt is not null) {
            claims.Add(new(Claims.Cnf, $"{{\"jkt\":\"{jkt}\"}}", JsonClaimValueTypes.Json));
        }

        var issuer = services.GetRequiredService<TokenService<SchemataSecurity>>();
        await using var signing = await issuer.BeginSigningAsync();
        return await SchemataAuthenticationHandler<SchemataApplication, SchemataSecurity, SchemataToken>.CreateTokenAsync(
            services.GetRequiredService<ITokenStore<SchemataToken>>(),
            services.GetRequiredService<IApplicationManager<SchemataApplication>>(),
            issuer,
            signing,
            claims,
            TokenFormats.Jwt,
            Anchor,
            Anchor.AddHours(1),
            TokenTypes.AccessToken,
            app,
            app,
            null,
            null,
            new() {
                Subject = app,
                SubjectKind = GrantSubjectKinds.EndUser,
                Profile = GrantProfiles.OpenIdConnect,
                Source = GrantTypes.AuthorizationCode,
                Scope = $"{Scopes.OpenId} {Scopes.Profile}",
                Authentication = new(acr, [], authTime),
            },
            default);
    }

    private static async Task<string> AppName(WebAppFactory factory) {
        using var scope = factory.Services.CreateScope();
        var apps = scope.ServiceProvider.GetRequiredService<IApplicationManager<SchemataApplication>>();
        return (await apps.FindByClientIdAsync("test-client"))!.CanonicalName!;
    }

    private static async Task<string> Nonce(WebAppFactory factory) {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var nonces = services.GetRequiredKeyedService<ITokenStore<SchemataToken>>(SecurityConstants.TokenTypes.Nonce);
        var ttl = services.GetRequiredService<IOptions<DPopOptions>>().Value.NonceLifetime;
        return (await nonces.GetOrCreateAsync(null, "dpop-rs", await AppName(factory), null, ttl, default)).Value!;
    }

    private static (string Proof, string Jkt) Proof(RSA key, string? token = null, string? nonce = null) {
        var parameters = key.ExportParameters(false);
        var jwk = new Dictionary<string, object> {
            ["kty"] = "RSA",
            ["n"] = Base64UrlEncoder.Encode(parameters.Modulus!),
            ["e"] = Base64UrlEncoder.Encode(parameters.Exponent!),
        };
        var claims = new Dictionary<string, object> {
            ["jti"] = Guid.NewGuid().ToString(),
            ["htm"] = "GET",
            ["htu"] = Htu,
            ["iat"] = Anchor.ToUnixTimeSeconds(),
        };
        if (token is not null) {
            claims["ath"] = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(token)));
        }
        if (nonce is not null) {
            claims["nonce"] = nonce;
        }

        var proof = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor {
            TokenType = TokenMediaTypes.DpopJwt,
            Claims = claims,
            SigningCredentials = new(new RsaSecurityKey(key), "RS256"),
            AdditionalHeaderClaims = new Dictionary<string, object> { ["jwk"] = jwk },
        });
        var canonical = "{"
                      + string.Join(",", jwk.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                                            .Select(pair => $"\"{pair.Key}\":\"{pair.Value}\""))
                      + "}";
        var jkt = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return (proof, jkt);
    }

    private static JsonElement Payload(string jwt) {
        using var document = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(jwt.Split('.')[1]));
        return document.RootElement.Clone();
    }

    private sealed class StaticRequirementProvider(ResourceAuthenticationRequirement? requirement)
        : IResourceAuthenticationRequirementProvider
    {
        public Task<ResourceAuthenticationRequirement?> EvaluateAsync(
            ResourceAuthenticationRequirementContext context,
            CancellationToken ct = default
        ) {
            return Task.FromResult(requirement);
        }
    }

    private sealed class StepUpSessionHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
            if (Request.Headers.Authorization != SessionScheme) {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var steppedUp = Request.Headers["X-Step-Up"] == "true";
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new(IdentityClaims.Subject, "users/u-1"),
                new(Claims.Acr, steppedUp ? Multifactor : Password),
                new(Claims.Amr, steppedUp ? "[\"pwd\",\"otp\",\"mfa\"]" : "[\"pwd\"]", JsonClaimValueTypes.Json),
                new(Claims.AuthTime, (steppedUp ? FreshAuthTime : StaleAuthTime).ToString(), ClaimValueTypes.Integer64),
            ], SessionScheme));
            return Task.FromResult(AuthenticateResult.Success(new(principal, SessionScheme)));
        }
    }
}
