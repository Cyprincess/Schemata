using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Integration.Tests;

/// <summary>
///     RFC 7662 §2 over real HTTP: an access token reports active only to an introspection caller
///     whose configured resource mapping covers one of the token's audiences; a mismatching or
///     missing mapping yields the opaque <c>{ "active": false }</c>, while refresh tokens stay
///     governed by token validity alone.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Layer", "Component")]
public class IntrospectionApplicabilityShould
{
    private const string RedirectUri  = "https://localhost/callback";
    private const string SessionScheme = "ManagementTest";
    private const string Challenge    = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";
    private const string Verifier     = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

    [Fact]
    public async Task Report_Active_When_The_Caller_Mapping_Covers_The_Token_Audience() {
        using var factory = new WebAppFactory();
        var       client  = factory.CreateClient();
        var       access  = await Mint(client);

        using var response = await Introspect(client, access, "introspect-client", "introspect-secret");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        Assert.True(body.GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task Report_Only_Active_False_When_The_Caller_Mapping_Does_Not_Cover_The_Token_Audience() {
        using var factory = new WebAppFactory();
        var       client  = factory.CreateClient();
        var       access  = await Mint(client);

        using var response = await Introspect(client, access, "foreign-introspect-client", "foreign-introspect-secret");

        await AssertOpaqueInactive(response);
    }

    [Fact]
    public async Task Report_Only_Active_False_When_The_Caller_Has_No_Mapping() {
        using var factory = new WebAppFactory();
        var       client  = factory.CreateClient();
        var       access  = await Mint(client);

        using var response = await Introspect(client, access, "unmapped-introspect-client", "unmapped-introspect-secret");

        await AssertOpaqueInactive(response);
    }

    [Fact]
    public async Task Report_A_Refresh_Token_Active_Without_Audience_Mapping() {
        using var factory = new WebAppFactory().WithEnvironment("Authenticated").WithServices(services => {
            services.AddAuthentication(SessionScheme)
                    .AddScheme<AuthenticationSchemeOptions, SessionHandler>(SessionScheme, null);
        });
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new(SessionScheme);

        var code = await Approve(client);
        using var exchange = await client.SendAsync(Token(code));
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        var refresh = JsonDocument.Parse(await exchange.Content.ReadAsStreamAsync()).RootElement
                                  .GetProperty("refresh_token").GetString();
        Assert.NotNull(refresh);

        using var response = await Introspect(
            factory.CreateClient(), refresh, "unmapped-introspect-client", "unmapped-introspect-secret");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        Assert.True(body.GetProperty("active").GetBoolean());
    }

    private static async Task<string> Mint(HttpClient client) {
        using var response = await client.PostAsync(Endpoints.Token, new FormUrlEncodedContent(new Dictionary<string, string> {
            ["grant_type"]    = GrantTypes.ClientCredentials,
            ["client_id"]     = "test-client",
            ["client_secret"] = "test-secret",
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var access = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement
                                 .GetProperty("access_token").GetString();
        Assert.NotNull(access);
        return access;
    }

    private static async Task<string> Approve(HttpClient client) {
        var url = "/connect/authorize?client_id=code-client"
                + "&redirect_uri=" + Uri.EscapeDataString(RedirectUri)
                + "&response_type=code&state=xyz&scope=openid%20offline_access"
                + "&code_challenge=" + Challenge
                + "&code_challenge_method=S256";

        var authorize = await client.GetAsync(url);
        Assert.True(HttpStatusCode.Found == authorize.StatusCode, await authorize.Content.ReadAsStringAsync());
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(authorize.Headers.Location!.Query);
        var approve = await client.PostAsync(
            "/connect/interact",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["code"]      = query[Parameters.Code]!,
                ["code_type"] = query["code_type"]!,
            }));
        Assert.True(HttpStatusCode.Found == approve.StatusCode, await approve.Content.ReadAsStringAsync());

        var callback = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(approve.Headers.Location!.Query);
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

    private static Task<HttpResponseMessage> Introspect(HttpClient client, string token, string id, string secret) {
        return client.PostAsync("/connect/introspect", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["token"]         = token,
            ["client_id"]     = id,
            ["client_secret"] = secret,
        }));
    }

    private static async Task AssertOpaqueInactive(HttpResponseMessage response) {
        // RFC 7662 §2.2: an audience-inapplicable token is indistinguishable from an unknown one —
        // the body carries active:false and no token metadata.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body     = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        var property = Assert.Single(body.EnumerateObject());
        Assert.Equal("active", property.Name);
        Assert.False(property.Value.GetBoolean());
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
            return Task.FromResult(AuthenticateResult.Success(new(principal, SessionScheme)));
        }
    }
}
