using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Component")]
public class HistoricalBrowserFlowShould
{
    private const string RedirectUri = "https://rp.example/cb";
    private const string Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

    [Fact]
    public async Task Omitted_Mode_Uses_Fragment_For_Hybrid_Error() {
        using var host = new WebAppFactory().WithHistoricalFlows().WithAuthenticatedInteraction();
        await SeedAsync(host, "hybrid-not-registered", [ResponseTypes.Code], [GrantTypes.AuthorizationCode]);
        using var client = host.CreateClient(new() { AllowAutoRedirect = false });

        using var response = await client.GetAsync(AuthorizeUrl("hybrid-not-registered", "code id_token"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(RedirectUri, response.Headers.Location!.GetLeftPart(UriPartial.Path));
        var fragment = HttpUtility.ParseQueryString(response.Headers.Location.Fragment.TrimStart('#'));
        Assert.Equal(OAuthErrors.UnauthorizedClient, fragment[Parameters.Error]);
        Assert.Equal("s-1", fragment[Parameters.State]);
        Assert.Equal(string.Empty, response.Headers.Location.Query);
    }

    [Fact]
    public async Task Pure_Implicit_IdToken_Uses_Discovery_Interaction_And_Fragment_Callback() {
        using var host = new WebAppFactory().WithHistoricalFlows().WithAuthenticatedInteraction();
        await SeedAsync(host, "implicit-client", [ResponseTypes.IdToken], [GrantTypes.Implicit]);
        using var client = host.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new(WebAppFactory.InteractionScheme);

        using var discoveryResponse = await client.GetAsync("/.well-known/openid-configuration");
        Assert.Equal(HttpStatusCode.OK, discoveryResponse.StatusCode);
        using var discovery = await JsonDocument.ParseAsync(await discoveryResponse.Content.ReadAsStreamAsync());
        Assert.Contains(ResponseTypes.IdToken, Values(discovery.RootElement, "response_types_supported"));
        Assert.Contains(GrantTypes.Implicit, Values(discovery.RootElement, "grant_types_supported"));
        Assert.Contains(ResponseModes.Fragment, Values(discovery.RootElement, "response_modes_supported"));

        using var callback = await ApproveAsync(
            client,
            AuthorizeUrl("implicit-client", ResponseTypes.IdToken),
            ResponseModes.Fragment);

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal(RedirectUri, callback.Headers.Location!.GetLeftPart(UriPartial.Path));
        Assert.Equal(string.Empty, callback.Headers.Location.Query);
        var fragment = HttpUtility.ParseQueryString(callback.Headers.Location.Fragment.TrimStart('#'));
        Assert.Equal("s-1", fragment[Parameters.State]);
        Assert.Null(fragment[Parameters.Code]);
        Assert.Null(fragment[Parameters.AccessToken]);
        var idToken = fragment[Parameters.IdToken];
        Assert.False(string.IsNullOrWhiteSpace(idToken));
        var jwt = new JsonWebToken(idToken);
        Assert.Equal("https://localhost", jwt.Issuer);
        Assert.Equal("implicit-client", Assert.Single(jwt.Audiences));
        Assert.Equal("users/u-1", jwt.Subject);
        Assert.Equal("n-1", jwt.GetClaim("nonce").Value);
    }

    [Fact]
    public async Task Explicit_FormPost_For_None_Returns_State_Without_Tokens() {
        using var host = new WebAppFactory().WithHistoricalFlows().WithAuthenticatedInteraction();
        await SeedAsync(host, "none-client", [ResponseTypes.None], []);
        using var client = host.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new(WebAppFactory.InteractionScheme);

        using var callback = await ApproveAsync(
            client,
            AuthorizeUrl("none-client", ResponseTypes.None, ResponseModes.FormPost),
            ResponseModes.FormPost);

        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        Assert.Null(callback.Headers.Location);
        var html = await callback.Content.ReadAsStringAsync();
        Assert.Contains("name=\"state\"", html);
        Assert.Contains("value=\"s-1\"", html);
        Assert.DoesNotContain("name=\"code\"", html);
        Assert.DoesNotContain("name=\"access_token\"", html);
        Assert.DoesNotContain("name=\"id_token\"", html);
    }

    [Fact]
    public async Task None_Cannot_Be_Combined_With_Another_Response_Type() {
        using var host = new WebAppFactory().WithHistoricalFlows();
        await SeedAsync(host, "none-client", [ResponseTypes.None, "code none"], [GrantTypes.AuthorizationCode]);
        using var client = host.CreateClient(new() { AllowAutoRedirect = false });

        using var response = await client.GetAsync(AuthorizeUrl("none-client", "none code"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var query = HttpUtility.ParseQueryString(response.Headers.Location!.Query);
        Assert.Equal(OAuthErrors.InvalidRequest, query[Parameters.Error]);
        Assert.Equal("s-1", query[Parameters.State]);
        Assert.Equal(string.Empty, response.Headers.Location.Fragment);
    }

    [Fact]
    public async Task Omitted_Mode_With_A_Disallowed_Default_Is_A_Bare_400() {
        using var host = new WebAppFactory().WithHistoricalFlows().WithServices(services => {
            services.Configure<SchemataAuthorizationOptions>(options => {
                options.AllowedResponseModes.Clear();
                options.AllowedResponseModes.Add(ResponseModes.FormPost);
            });
        });
        await SeedAsync(host, "hybrid-client", ["code id_token"], [GrantTypes.AuthorizationCode, GrantTypes.Implicit]);
        using var client = host.CreateClient(new() { AllowAutoRedirect = false });

        using var response = await client.GetAsync(AuthorizeUrl("hybrid-client", "code id_token"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Pkce_Rejection_Uses_The_Validated_Hybrid_Fragment() {
        using var host = new WebAppFactory().WithHistoricalFlows().WithServices(services => {
            services.Configure<CodeFlowOptions>(options => {
                options.RequirePkce = true;
                options.RequirePkceS256 = true;
            });
        });
        await SeedAsync(host, "hybrid-client", ["code id_token"], [GrantTypes.AuthorizationCode, GrantTypes.Implicit]);
        using var client = host.CreateClient(new() { AllowAutoRedirect = false });

        using var response = await client.GetAsync(AuthorizeUrl("hybrid-client", "code id_token"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var fragment = HttpUtility.ParseQueryString(response.Headers.Location!.Fragment.TrimStart('#'));
        Assert.Equal(OAuthErrors.InvalidRequest, fragment[Parameters.Error]);
        Assert.Equal("s-1", fragment[Parameters.State]);
    }

    [Fact]
    public async Task Missing_Implicit_Grant_Uses_The_Validated_Hybrid_Fragment() {
        using var host = new WebAppFactory().WithHistoricalFlows();
        await SeedAsync(host, "hybrid-client", ["code id_token"], [GrantTypes.AuthorizationCode]);
        using var client = host.CreateClient(new() { AllowAutoRedirect = false });

        using var response = await client.GetAsync(AuthorizeUrl("hybrid-client", "code id_token", pkce: true));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var fragment = HttpUtility.ParseQueryString(response.Headers.Location!.Fragment.TrimStart('#'));
        Assert.Equal(OAuthErrors.UnauthorizedClient, fragment[Parameters.Error]);
        Assert.Equal("s-1", fragment[Parameters.State]);
    }

    [Fact]
    public async Task Invalid_Redirect_Never_Receives_A_Callback() {
        using var host = new WebAppFactory().WithHistoricalFlows();
        await SeedAsync(host, "implicit-client", [ResponseTypes.IdToken], [GrantTypes.Implicit]);
        using var client = host.CreateClient(new() { AllowAutoRedirect = false });

        using var response = await client.GetAsync(
            AuthorizeUrl("implicit-client", ResponseTypes.IdToken).Replace(
                Uri.EscapeDataString(RedirectUri),
                Uri.EscapeDataString("https://evil.example/cb"),
                StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
        using var error = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal(OAuthErrors.InvalidRequest, error.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Disabled_Historical_Flows_Are_Not_Advertised_Or_Executed() {
        using var host = new WebAppFactory();
        await SeedAsync(host, "implicit-client", [ResponseTypes.IdToken], [GrantTypes.Implicit]);
        using var client = host.CreateClient(new() { AllowAutoRedirect = false });

        using var discoveryResponse = await client.GetAsync("/.well-known/openid-configuration");
        using var discovery = await JsonDocument.ParseAsync(await discoveryResponse.Content.ReadAsStreamAsync());
        Assert.DoesNotContain(ResponseTypes.IdToken, Values(discovery.RootElement, "response_types_supported"));
        Assert.DoesNotContain("code id_token", Values(discovery.RootElement, "response_types_supported"));
        Assert.DoesNotContain(ResponseTypes.None, Values(discovery.RootElement, "response_types_supported"));
        Assert.DoesNotContain(GrantTypes.Implicit, Values(discovery.RootElement, "grant_types_supported"));

        using var response = await client.GetAsync(AuthorizeUrl("implicit-client", ResponseTypes.IdToken));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    private static string AuthorizeUrl(
        string clientId,
        string responseType,
        string? responseMode = null,
        bool pkce = false
    ) {
        var url = "/connect/authorize?client_id=" + Uri.EscapeDataString(clientId)
                + "&redirect_uri=" + Uri.EscapeDataString(RedirectUri)
                + "&response_type=" + Uri.EscapeDataString(responseType)
                + "&scope=openid&state=s-1&nonce=n-1";
        if (responseMode is not null) {
            url += "&response_mode=" + Uri.EscapeDataString(responseMode);
        }

        if (pkce) {
            url += "&code_challenge=" + Challenge + "&code_challenge_method=S256";
        }

        return url;
    }

    private static async Task SeedAsync(
        WebAppFactory host,
        string clientId,
        string[] responseTypes,
        string[] grantTypes
    ) {
        await using var scope = host.Services.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IApplicationManager<SchemataApplication>>();
        await applications.CreateAsync(new() {
            Name = clientId,
            ClientId = clientId,
            TokenEndpointAuthMethod = ClientAuthMethods.None,
            RedirectUris = [RedirectUri],
            Permissions = ["e:/Connect/Authorize"],
            ResponseTypes = responseTypes,
            GrantTypes = grantTypes,
            Scope = Scopes.OpenId,
        });
    }

    private static async Task<HttpResponseMessage> ApproveAsync(
        HttpClient client,
        string authorizeUrl,
        string expectedMode
    ) {
        using var authorize = await client.GetAsync(authorizeUrl);
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        var location = authorize.Headers.Location!;
        Assert.Equal("https://localhost/interact", location.GetLeftPart(UriPartial.Path));
        var interaction = HttpUtility.ParseQueryString(location.Query);

        using var details = await client.GetAsync("/connect/interact" + location.Query);
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        using var document = await JsonDocument.ParseAsync(await details.Content.ReadAsStreamAsync());
        Assert.Equal(expectedMode,
            document.RootElement.GetProperty("request").GetProperty("response_mode").GetString());

        return await client.PostAsync("/connect/interact", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["code"] = interaction[Parameters.Code]!,
            ["code_type"] = interaction["code_type"]!,
        }));
    }

    private static string[] Values(JsonElement document, string property) {
        return [.. document.GetProperty(property).EnumerateArray().Select(value => value.GetString()!)];
    }
}
