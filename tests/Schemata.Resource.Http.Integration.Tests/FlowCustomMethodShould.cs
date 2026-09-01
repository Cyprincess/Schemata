using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton.Models;
using Schemata.Resource.Http.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Http.Integration.Tests;

[Trait("Category", "Integration")]
public class FlowCustomMethodShould : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public FlowCustomMethodShould(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task StartProcess_Unknown_Definition_Returns_NotFound() {
        var response = await _factory.CreateClient().PostAsync(
            "/v1/processes:start",
            new StringContent("""{"definition_name":"missing"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Process definition", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompleteProcess_Missing_Instance_Returns_NotFound() {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/v1/processes/missing:complete", new CompleteActivityRequest());
        await AssertResourceMethodNotFound(response);
    }

    [Fact]
    public async Task CorrelateProcess_Missing_Instance_Returns_NotFound() {
        var response = await _factory.CreateClient().PostAsync(
            "/v1/processes/missing:correlate",
            new StringContent("""{"message_name":"approved"}""", Encoding.UTF8, "application/json"));
        await AssertResourceMethodNotFound(response);
    }

    [Fact]
    public async Task SignalProcess_Empty_Broadcast_Returns_Ok() {
        var response = await _factory.CreateClient().PostAsync(
            "/v1/processes:signal",
            new StringContent("""{"signal_name":"approved"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TerminateProcess_Missing_Instance_Returns_NotFound() {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/v1/processes/missing:terminate", new TerminateProcessResourceRequest());
        await AssertResourceMethodNotFound(response);
    }

    [Fact]
    public async Task CancelToken_Missing_Instance_Returns_NotFound() {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/v1/processes/missing/tokens/missing:cancel", new CancelTokenResourceRequest());
        await AssertResourceMethodNotFound(response);
    }

    private static async Task AssertResourceMethodNotFound(HttpResponseMessage response) {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("RESOURCE_NOT_FOUND", body, StringComparison.OrdinalIgnoreCase);
    }
}
