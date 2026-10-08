using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Foundation.Commands;
using FlowModels = Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Integration.Tests.Resource.Fixtures;
using Xunit;

namespace Schemata.Flow.Integration.Tests.Resource;

[Trait("Category", "Integration")]
public class FlowCustomMethodShould : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public FlowCustomMethodShould(WebAppFactory factory) { _factory = factory; }


    [Fact]
    public async Task Start_Exact_Versions_And_List_Distinct_Registered_Graphs() {
        var registry = _factory.Services.GetRequiredService<IProcessRegistry>();
        await registry.RegisterAsync<ProcessVersionShould.Original>(configure: c => { c.Name = "wire-version"; c.Version = "one"; });
        await registry.RegisterAsync<ProcessVersionShould.Replacement>(configure: c => { c.Name = "wire-version"; c.Version = "two"; c.IsLatest = true; });
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/v1/processes:start",
            new StringContent("""{"definition_name":"wire-version","definition_version":"one"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("one", body.RootElement.GetProperty("definition_version").GetString());
        var name = body.RootElement.GetProperty("name").GetString();
        var completed = await client.PostAsJsonAsync($"/v1/{name}:complete", new FlowModels.CompleteActivityRequest());
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        using var listing = JsonDocument.Parse(await client.GetStringAsync("/v1/processes:definitions"));
        Assert.Contains(listing.RootElement.GetProperty("versions").EnumerateArray(), item => item.GetProperty("name").GetString() == "definitions/wire-version/versions/one");
        Assert.Contains(listing.RootElement.GetProperty("versions").EnumerateArray(), item => item.GetProperty("name").GetString() == "definitions/wire-version/versions/two");
    }
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
            "/v1/processes/missing:complete", new FlowModels.CompleteActivityRequest());
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
