using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.WebUtilities;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Integration.Tests;

/// <summary>
///     OpenID Connect Core 1.0 §5.5 (the claims request parameter) and §5.3.2 (signed and
///     encrypted UserInfo responses) over the real authorization-code flow.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Layer", "Component")]
public class ClaimsParameterShould
{
    private const string RedirectUri = "https://localhost/callback";
    private const string SessionScheme = "ManagementTest";

    private const string Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";
    private const string Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

    [Fact]
    public async Task Advertise_Claims_Parameter_Support_In_The_Discovery_Document() {
        using var factory = new WebAppFactory();

        var response = await factory.CreateClient().GetAsync("/.well-known/openid-configuration");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        Assert.True(document.GetProperty("claims_parameter_supported").GetBoolean());
    }

    [Fact]
    public async Task Reject_A_Malformed_Claims_Parameter_With_Invalid_Request() {
        using var factory = New_Factory();
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync(Authorize("openid", claims: "{not-json"));

        // RFC 6749 §4.1.2.1: the error returns to the redirect URI with the state intact.
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var query = QueryHelpers.ParseQuery(response.Headers.Location!.Query);
        Assert.Equal(OAuthErrors.InvalidRequest, query[Parameters.Error]);
        Assert.Equal("xyz", query[Parameters.State]);
    }

    [Fact]
    public async Task Reject_A_Pinned_Subject_Mismatch_Under_Prompt_None() {
        using var factory = New_Factory();
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new(SessionScheme);

        // First establish the consent record so the silent request reaches the claims checks.
        await Approve(client, "openid", null);

        var claims = JsonSerializer.Serialize(new {
            id_token = new Dictionary<string, object> {
                ["sub"] = new { value = "users/someone-else" },
            },
        });

        var response = await client.GetAsync(Authorize("openid", claims, prompt: "none"));

        // §5.5.1: a sub value mismatch MUST cause the authentication to fail; silently.
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var query = QueryHelpers.ParseQuery(response.Headers.Location!.Query);
        Assert.Equal(OAuthErrors.LoginRequired, query[Parameters.Error]);
    }

    [Fact]
    public async Task Carry_The_Request_Through_The_Code_Exchange_Into_The_Tokens() {
        using var factory = New_Factory(TokenFormats.Jwt);
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new(SessionScheme);

        var claims = JsonSerializer.Serialize(new {
            id_token = new Dictionary<string, object?> {
                ["sub"] = null,
            },
            userinfo = new Dictionary<string, object?> {
                ["sub"] = null,
            },
        });

        var code = await Approve(client, "openid", claims);

        var response = await client.SendAsync(Token(code));
        Assert.True(HttpStatusCode.OK == response.StatusCode, await response.Content.ReadAsStringAsync());

        var pair        = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        var accessToken = pair.GetProperty("access_token").GetString();
        Assert.NotNull(accessToken);

        // §5.5: the userinfo member's requested claim names ride the access token so the
        // UserInfo endpoint can widen its output beyond the scope-derived set.
        var payload = Payload(accessToken!);
        Assert.Equal("sub", payload.GetProperty(Claims.UserinfoRequest).GetString());

        using var bearer = new HttpRequestMessage(HttpMethod.Get, Endpoints.Profile);
        bearer.Headers.Authorization = new(Schemes.Bearer, accessToken);
        var userinfo = await factory.CreateClient().SendAsync(bearer);

        Assert.True(HttpStatusCode.OK == userinfo.StatusCode, await userinfo.Content.ReadAsStringAsync());
        var body = JsonDocument.Parse(await userinfo.Content.ReadAsStreamAsync()).RootElement;
        Assert.Equal("users/u-1", body.GetProperty(IdentityClaims.Subject).GetString());
    }

    [Fact]
    public async Task Return_A_Signed_UserInfo_Jwt_For_A_Registered_Client() {
        using var factory = New_Factory();
        await RegisterProtectionAsync(factory, "code-client", signed: true);

        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new(SessionScheme);

        var code = await Approve(client, "openid", null);

        var response = await client.SendAsync(Token(code));
        Assert.True(HttpStatusCode.OK == response.StatusCode, await response.Content.ReadAsStringAsync());
        var accessToken = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement
                                      .GetProperty("access_token").GetString();
        Assert.NotNull(accessToken);

        using var bearer = new HttpRequestMessage(HttpMethod.Get, Endpoints.Profile);
        bearer.Headers.Authorization = new(Schemes.Bearer, accessToken);
        var userinfo = await factory.CreateClient().SendAsync(bearer);

        Assert.True(HttpStatusCode.OK == userinfo.StatusCode, await userinfo.Content.ReadAsStringAsync());

        // §5.3.2: the JWT serialization carries the application/jwt content type.
        Assert.Equal("application/jwt", userinfo.Content.Headers.ContentType!.MediaType);

        var jwt = await userinfo.Content.ReadAsStringAsync();
        var payload = Payload(jwt);

        // §5.3.2: a signed response contains iss (the OP) and aud (the client id).
        Assert.Equal("https://localhost", payload.GetProperty(Claims.Issuer).GetString());
        Assert.Equal("code-client", payload.GetProperty(Claims.Audience).GetString());
        Assert.Equal("users/u-1", payload.GetProperty(IdentityClaims.Subject).GetString());

        // The signature verifies with the issuer's own validation path and the client audience.
        using var scope = factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        var principal = await tokens.Validate(jwt, "code-client");
        Assert.NotNull(principal);
    }

    [Fact]
    public async Task Return_Plain_Json_When_No_Protection_Is_Registered() {
        using var factory = New_Factory();
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new(SessionScheme);

        var code = await Approve(client, "openid", null);

        var response = await client.SendAsync(Token(code));
        Assert.True(HttpStatusCode.OK == response.StatusCode, await response.Content.ReadAsStringAsync());
        var accessToken = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement
                                      .GetProperty("access_token").GetString();
        Assert.NotNull(accessToken);

        using var bearer = new HttpRequestMessage(HttpMethod.Get, Endpoints.Profile);
        bearer.Headers.Authorization = new(Schemes.Bearer, accessToken);
        var userinfo = await factory.CreateClient().SendAsync(bearer);

        Assert.True(HttpStatusCode.OK == userinfo.StatusCode, await userinfo.Content.ReadAsStringAsync());
        Assert.Equal("application/json", userinfo.Content.Headers.ContentType!.MediaType);
    }

    private static async Task RegisterProtectionAsync(WebAppFactory factory, string clientId, bool signed) {
        using var scope = factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        var app = await context.Set<SchemataApplication>().SingleAsync(a => a.ClientId == clientId);
        app.UserinfoSignedResponseAlg = signed ? "RS256" : null;
        await context.SaveChangesAsync();
    }

    private static string Authorize(string scope, string? claims, string? prompt = null) {
        return "/connect/authorize?client_id=code-client"
             + "&redirect_uri=" + Uri.EscapeDataString(RedirectUri)
             + "&response_type=code&state=xyz"
             + "&scope=" + Uri.EscapeDataString(scope)
             + (prompt is null ? string.Empty : "&prompt=" + Uri.EscapeDataString(prompt))
             + (claims is null ? string.Empty : "&claims=" + Uri.EscapeDataString(claims))
             + "&code_challenge=" + Challenge
             + "&code_challenge_method=S256";
    }

    private static async Task<string> Approve(HttpClient client, string scope, string? claims, string? prompt = null) {
        var authorize = await client.GetAsync(Authorize(scope, claims, prompt));
        Assert.True(HttpStatusCode.Found == authorize.StatusCode, await authorize.Content.ReadAsStringAsync());
        var location = authorize.Headers.Location!;
        Assert.Equal("https://localhost/interact", location.GetLeftPart(UriPartial.Path));
        var query = QueryHelpers.ParseQuery(location.Query);

        var approve = await client.PostAsync(
            "/connect/interact",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["code"]      = query[Parameters.Code]!,
                ["code_type"] = query["code_type"]!,
            }));
        Assert.True(HttpStatusCode.Found == approve.StatusCode, await approve.Content.ReadAsStringAsync());

        var callback = QueryHelpers.ParseQuery(approve.Headers.Location!.Query);
        return callback[Parameters.Code]!;
    }

    private static HttpRequestMessage Token(string code) {
        return new(HttpMethod.Post, Endpoints.Token) {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                ["grant_type"]    = GrantTypes.AuthorizationCode,
                ["client_id"]     = "code-client",
                ["client_secret"] = "code-secret",
                ["code"]          = code,
                ["redirect_uri"]  = RedirectUri,
                ["code_verifier"] = Verifier,
            }),
        };
    }

    private static JsonElement Payload(string jwt) {
        var segments = jwt.Split('.');
        return JsonDocument.Parse(Base64UrlDecode(segments[1])).RootElement.Clone();
    }

    private static byte[] Base64UrlDecode(string value) {
        return Microsoft.IdentityModel.Tokens.Base64UrlEncoder.DecodeBytes(value);
    }

    private static WebAppFactory New_Factory(string? format = null) {
        return new WebAppFactory().WithEnvironment("Authenticated").WithServices(services => {
            services.AddAuthentication(SessionScheme)
                    .AddScheme<AuthenticationSchemeOptions, SessionHandler>(SessionScheme, null);
            if (format is not null) {
                services.PostConfigure<SchemataAuthorizationOptions>(o => o.AccessTokenFormat = format);
            }
        });
    }

    private sealed class SessionHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory                               logger,
        UrlEncoder                                   encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
            if (Request.Headers.Authorization != SessionScheme) {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new(IdentityClaims.Subject, "users/u-1"),
            ], SessionScheme));
            return Task.FromResult(AuthenticateResult.Success(new(principal, Scheme.Name)));
        }
    }
}
