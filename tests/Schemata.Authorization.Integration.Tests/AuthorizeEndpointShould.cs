using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Xunit;

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

        Assert.NotEqual(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains("login_required", await response.Content.ReadAsStringAsync());
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
