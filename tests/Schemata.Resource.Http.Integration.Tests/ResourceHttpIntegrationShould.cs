using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Schemata.Resource.Http.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Http.Integration.Tests;

[Trait("Category", "Integration")]
public class ResourceHttpIntegrationShould : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public ResourceHttpIntegrationShould(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Get_AllStudents_Returns200WithList() {
        var client   = _factory.CreateClient();
        var response = await client.GetAsync("/students");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Post_NewStudent_Returns201() {
        var client   = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/students", new Student { FullName = "Test" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Post_NewStudent_ResponseBodyContainsStudent() {
        var client   = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/students", new Student { FullName = "Returned" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json    = await response.Content.ReadFromJsonAsync<JsonElement>();
        var hasName = json.TryGetProperty("name", out var nameProp) || json.TryGetProperty("Name", out nameProp);

        Assert.True(hasName, "Response should contain a 'name' property");
        Assert.False(string.IsNullOrWhiteSpace(nameProp.GetString()), "Name should be non-empty");
    }

    [Fact]
    public async Task Delete_ExistingStudent_Returns204() {
        var client  = _factory.CreateClient();
        var created = await client.PostAsJsonAsync("/students", new Student { FullName = "ToDelete" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var body    = await created.Content.ReadFromJsonAsync<JsonElement>();
        var gotName = body.TryGetProperty("name", out var nameProp) || body.TryGetProperty("Name", out nameProp);
        Assert.True(gotName);

        var name     = nameProp.GetString()!;
        var response = await client.DeleteAsync($"/{name}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
