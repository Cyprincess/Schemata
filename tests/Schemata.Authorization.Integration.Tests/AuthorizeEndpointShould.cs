using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Component")]
public class AuthorizeEndpointShould : IClassFixture<WebAppFactory>
{
    private const string Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

    private readonly HttpClient _client;

    public AuthorizeEndpointShould(WebAppFactory factory) {
        _client = factory.CreateClient(new() { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task RedirectToTheInteractionUri_WhenThereIsNoSession() {
        var response = await _client.GetAsync(Authorize());

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        var location = response.Headers.Location!;
        Assert.Equal("https://localhost/interact", location.GetLeftPart(UriPartial.Path));

        var query = HttpUtility.ParseQueryString(location.Query);
        Assert.False(string.IsNullOrWhiteSpace(query["code"]));
        Assert.False(string.IsNullOrWhiteSpace(query["code_type"]));
    }

    [Fact]
    public async Task RedirectToTheInteractionUri_WhenPromptIsLogin() {
        var response = await _client.GetAsync($"{Authorize()}&prompt=login");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("https://localhost/interact", response.Headers.Location!.GetLeftPart(UriPartial.Path));
    }

    [Fact]
    public async Task StillRaiseLoginRequired_WhenPromptIsNone() {
        var response = await _client.GetAsync($"{Authorize()}&prompt=none");

        // OIDC Core 3.1.2.6: a prompt=none authentication failure returns login_required through
        // the validated authorization redirect (issue #135 finalization), not a JSON body.
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var query = HttpUtility.ParseQueryString(response.Headers.Location!.Query);
        Assert.Equal("login_required", query["error"]);
        Assert.Equal("xyz", query["state"]);
    }

    [Fact]
    public async Task Answer_An_Unsupported_Response_Mode_With_A_Parameter_Free_400_And_No_Callback() {
        // OIDC Core §3.1.2.6: an unsupported response mode has no legal delivery encoding, so
        // the endpoint answers the bare HTTP 400 — empty body, no Location.
        var response = await _client.GetAsync($"{Authorize()}&response_mode=carrier-pigeon");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Answer_An_Unsupported_Response_Mode_With_The_Par_Json_Error() {
        // RFC 9126 §2.3: the PAR endpoint answers request errors with the RFC 6749 §5.2 JSON
        // error response; the parameter-free 400 applies only to the interactive endpoint.
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> {
            [Parameters.ClientId]     = "code-client",
            [Parameters.ClientSecret] = "code-secret",
            [Parameters.RedirectUri]  = "https://localhost/callback",
            [Parameters.ResponseType] = "code",
            [Parameters.ResponseMode] = "carrier-pigeon",
        });

        using var response = await _client.PostAsync("/Connect/Par", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var error = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.Equal(OAuthErrors.InvalidRequest, error.RootElement.GetProperty("error").GetString());
    }

    private static string Authorize() {
        return "/connect/authorize"
             + "?client_id=browser-client"
             + "&redirect_uri=https%3A%2F%2Flocalhost%2Fcallback"
             + "&response_type=code"
             + "&state=xyz"
             + $"&code_challenge={Challenge}"
             + "&code_challenge_method=S256";
    }
}
