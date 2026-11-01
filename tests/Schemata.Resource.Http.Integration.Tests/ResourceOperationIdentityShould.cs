using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Schemata.Resource.Http.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Http.Integration.Tests;

/// <summary>
///     Issue #34 observable matrix over real MVC discovery: one scheme-protected resource whose
///     registered operations whitelist, anonymous exemptions, and custom verbs must behave the
///     same at the HTTP boundary as the registered operation identity dictates.
/// </summary>
public class ResourceOperationIdentityShould(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static readonly string Base = "/v1/lockedStudents";

    [Fact]
    public async Task AnonymousExemptOperation_PassesSchemeAuthentication() {
        var client = factory.CreateClient();

        // Get is [Anonymous] on the scheme-protected resource: real MVC discovery must keep the
        // exemption even though the display action name lost its Async suffix. The single-segment
        // name matches the discovered {name} route, so the handler — not the router — answers.
        var response = await client.GetAsync($"{Base}/missing");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("lockedStudents/missing", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SchemeOnlyOperation_RejectsAnonymousCallers() {
        var client = factory.CreateClient();

        var response = await client.GetAsync(Base);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SchemeOnlyOperation_AcceptsAuthenticatedCallers() {
        var client  = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, Base);
        request.Headers.Add("X-Test-Auth", "valid");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("POST", "{0}")]     // Create dropped by the whitelist
    [InlineData("DELETE", "{0}/x")] // Delete dropped by the whitelist
    public async Task WhitelistedOutOperations_AbsentFromDiscoveredEndpoints(string method, string template) {
        var client = factory.CreateClient();

        var response = await client.SendAsync(new(HttpMethod.Parse(method), string.Format(template, Base)));

        // A dropped action never produces an endpoint for the route: the address exists (other
        // verbs/selectors match the same path) but the method is not allowed; a whitelisted
        // protected operation instead answers with its authorization challenge.
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task WhitelistedUpdateOperation_ChallengesAnonymousCallers() {
        var client = factory.CreateClient();

        // Update stays in the discovered endpoints (registered [HttpPatch]), so an anonymous
        // caller receives the scheme challenge rather than 405.
        var response = await client.PatchAsync($"{Base}/x",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WhitelistedOutOperations_NeverAuthenticateOrBind() {
        var client = factory.CreateClient();

        // Delete is outside the whitelist: even authenticated callers get 405, not 401 or a
        // handler error — the endpoint simply does not exist.
        var request = new HttpRequestMessage(HttpMethod.Delete, $"{Base}/x");
        request.Headers.Add("X-Test-Auth", "valid");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousCustomVerb_PassesSchemeAuthentication() {
        var client = factory.CreateClient();

        var response = await client.PostAsync($"{Base}/missing:announce", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedCustomVerb_RejectsAnonymousCallers() {
        var client = factory.CreateClient();

        var response = await client.PostAsync($"{Base}/missing:seal", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
