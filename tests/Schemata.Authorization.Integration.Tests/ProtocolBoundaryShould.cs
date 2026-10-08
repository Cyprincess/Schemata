using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Layer", "Integration")]
public class ProtocolBoundaryShould
{
    [Theory]
    [InlineData("query")]
    [InlineData("form_post")]
    public async Task Par_Error_Is_Json_Without_Browser_Callback(string mode) {
        using var factory = new WebAppFactory().WithServices(services =>
            services.Configure<SchemataAuthorizationOptions>(options => options.AllowedResponseModes.Add("query")));
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        using var response = await client.PostAsync("/Connect/Par", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["client_id"] = "code-client", ["client_secret"] = "code-secret", ["response_type"] = "code",
            ["redirect_uri"] = "https://localhost/callback", ["scope"] = "unknown-scope",
            ["response_mode"] = mode, ["state"] = "private-state",
            ["code_challenge"] = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", ["code_challenge_method"] = "S256",
        }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("invalid_scope", body.RootElement.GetProperty("error").GetString());
        Assert.False(body.RootElement.TryGetProperty("state", out _));
    }

    [Theory]
    [InlineData("NoClaims", HttpStatusCode.Found)]
    [InlineData("Testing", HttpStatusCode.Found)]
    public async Task Claims_Parameter_Validation_Only_Runs_When_Enabled(string environment, HttpStatusCode status) {
        using var factory = new WebAppFactory().WithEnvironment(environment);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var uri = "/Connect/Authorize?client_id=code-client&response_type=code&redirect_uri="
            + Uri.EscapeDataString("https://localhost/callback")
            + "&scope=openid&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM&code_challenge_method=S256&claims="
            + Uri.EscapeDataString("{malformed");
        using var response = await client.GetAsync(uri);
        Assert.Equal(status, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        if (environment == "NoClaims") {
            Assert.Equal("/interact", response.Headers.Location.AbsolutePath);
            Assert.DoesNotContain("error=", response.Headers.Location.Query);
        } else {
            Assert.Equal("/callback", response.Headers.Location.AbsolutePath);
            Assert.Contains("error=invalid_request", response.Headers.Location.Query);
        }
    }
}
