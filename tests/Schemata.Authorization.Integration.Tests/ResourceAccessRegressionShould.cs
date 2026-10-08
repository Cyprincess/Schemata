using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Integration.Tests;

/// <summary>
///     End-to-end regression for the resource-server audience semantics (RFC 9068 §4):
///     access tokens minted by the real token endpoint must pass this server's own Bearer
///     validation at the UserInfo endpoint, and tokens audience-restricted to an external
///     resource must be rejected there.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Layer", "Component")]
public class ResourceAccessRegressionShould
{
    private const string RedirectUri = "https://localhost/callback";
    private const string SessionScheme = "ManagementTest";

    private const string Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";
    private const string Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

    [Fact]
    public async Task Accept_A_Real_Endpoint_Token_At_The_User_Info_Endpoint() {
        using var factory = New_Factory();
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new(SessionScheme);

        var code = await Approve(client);

        var response = await client.SendAsync(Token(code));
        Assert.True(HttpStatusCode.OK == response.StatusCode, await response.Content.ReadAsStringAsync());

        var pair        = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        var accessToken = pair.GetProperty("access_token").GetString();
        Assert.NotNull(accessToken);

        using var bearer = new HttpRequestMessage(HttpMethod.Get, Endpoints.Profile);
        bearer.Headers.Authorization = new(Schemes.Bearer, accessToken);
        var userinfo = await factory.CreateClient().SendAsync(bearer);

        Assert.True(HttpStatusCode.OK == userinfo.StatusCode, await userinfo.Content.ReadAsStringAsync());
        var body = JsonDocument.Parse(await userinfo.Content.ReadAsStreamAsync()).RootElement;
        Assert.Equal("users/u-1", body.GetProperty(IdentityClaims.Subject).GetString());
    }

    [Fact]
    public async Task Reject_A_Token_Audience_Restricted_To_An_External_Resource() {
        using var factory = New_Factory();
        var client = factory.CreateClient();

        var response = await client.SendAsync(new(HttpMethod.Post, Endpoints.Token) {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                ["grant_type"]    = GrantTypes.ClientCredentials,
                ["client_id"]     = "test-client",
                ["client_secret"] = "test-secret",
                ["resource"]      = "https://rs.example.com/api",
            }),
        });

        Assert.True(HttpStatusCode.OK == response.StatusCode, await response.Content.ReadAsStringAsync());
        var accessToken = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement
                                      .GetProperty("access_token").GetString();
        Assert.NotNull(accessToken);

        using var bearer = new HttpRequestMessage(HttpMethod.Get, Endpoints.Profile);
        bearer.Headers.Authorization = new(Schemes.Bearer, accessToken);
        var userinfo = await client.SendAsync(bearer);

        // RFC 9068 §4: the UserInfo endpoint is this server itself; a token audience-restricted
        // to https://rs.example.com/api does not name it and MUST be rejected.
        Assert.Equal(HttpStatusCode.Unauthorized, userinfo.StatusCode);
        Assert.Contains($"error=\"{OAuthErrors.InvalidToken}\"", userinfo.Headers.WwwAuthenticate.ToString());
    }

    private static async Task<string> Approve(HttpClient client) {
        var url = "/connect/authorize?client_id=code-client"
                + "&redirect_uri=" + Uri.EscapeDataString(RedirectUri)
                + "&response_type=code&state=xyz&scope=openid"
                + "&code_challenge=" + Challenge
                + "&code_challenge_method=S256";

        var authorize = await client.GetAsync(url);
        Assert.True(HttpStatusCode.Found == authorize.StatusCode, await authorize.Content.ReadAsStringAsync());
        var location = authorize.Headers.Location!;
        var query    = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(location.Query);
        var approve  = await client.PostAsync(
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

    private static WebAppFactory New_Factory() {
        return new WebAppFactory().WithEnvironment("Authenticated").WithServices(services => {
            services.AddAuthentication(SessionScheme)
                    .AddScheme<AuthenticationSchemeOptions, SessionHandler>(SessionScheme, null);
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
