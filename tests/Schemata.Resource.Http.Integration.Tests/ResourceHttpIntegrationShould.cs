using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
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
        var client = _factory.CreateClient();
        var created = await client.PostAsync("/v1/students",
                                             new StringContent("""{"full_name":"HttpListStudent"}""", Encoding.UTF8,
                                                               "application/json"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var response = await client.GetAsync("/v1/students");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // AIP-132/AIP-140: repeated results ride the plural collection field.
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.GetProperty("students").ValueKind);
        var listed = Assert.Single(body.GetProperty("students").EnumerateArray(),
                                   s => s.GetProperty("full_name").GetString() == "HttpListStudent");
        Assert.False(string.IsNullOrWhiteSpace(listed.GetProperty("name").GetString()));
        Assert.Equal(JsonValueKind.Number, body.GetProperty("total_size").ValueKind);
        Assert.True(body.GetProperty("total_size").GetInt32() >= 1);
        Assert.True(!body.TryGetProperty("next_page_token", out var token) || token.ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task Post_NewStudent_Returns201() {
        var client   = _factory.CreateClient();
        var response = await client.PostAsync("/v1/students",
                                              new StringContent("""{"full_name":"Test"}""", Encoding.UTF8,
                                                                "application/json"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Test", body.GetProperty("full_name").GetString());
        var name = body.GetProperty("name").GetString();
        Assert.False(string.IsNullOrWhiteSpace(name));
        Assert.StartsWith("students/", name);
        var uid = body.GetProperty("uid").GetString();
        Assert.NotNull(uid);
        Assert.NotEqual(Guid.Empty, Guid.Parse(uid));
    }

    [Fact]
    public async Task Delete_ExistingStudent_Returns204() {
        var client  = _factory.CreateClient();
        var created = await client.PostAsJsonAsync("/v1/students", new Student { FullName = "ToDelete" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var body    = await created.Content.ReadFromJsonAsync<JsonElement>();
        var gotName = body.TryGetProperty("name", out var nameProp) || body.TryGetProperty("Name", out nameProp);
        Assert.True(gotName);

        var name     = nameProp.GetString();
        Assert.NotNull(name);
        var response = await client.DeleteAsync($"/v1/{name}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_MissingStudent_WithoutAllowMissing_Returns404() {
        var client   = _factory.CreateClient();
        var response = await client.DeleteAsync("/v1/students/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_MissingStudent_WithAllowMissing_Returns204() {
        var client   = _factory.CreateClient();
        var response = await client.DeleteAsync("/v1/students/does-not-exist?allow_missing=true");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task GetCustomMethod_Preview_Returns200WithBody() {
        var client = _factory.CreateClient();
        var created = await client.PostAsync("/v1/students",
                                             new StringContent("""{"full_name":"Previewable"}""", Encoding.UTF8,
                                                               "application/json"));
        var body = await created.Content.ReadFromJsonAsync<Student>();

        Assert.NotNull(body);
        var response = await client.GetAsync($"/v1/{body.Name}:preview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Previewable", preview.GetProperty("full_name").GetString());
    }

    [Fact]
    public async Task GetCustomMethod_PostVerb_IsRejected() {
        var client  = _factory.CreateClient();
        var created = await client.PostAsJsonAsync("/v1/students", new Student { FullName = "PostRejected" });
        var body    = await created.Content.ReadFromJsonAsync<Student>();

        Assert.NotNull(body);
        var response = await client.PostAsJsonAsync($"/v1/{body.Name}:preview", new Student());

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task SoftDeleteUndeleteAndExpunge_Lifecycle_ReturnsExpectedStates() {
        var client = _factory.CreateClient();

        var created = await client.PostAsync("/v1/trashes",
                                             new StringContent("""{"full_name":"Disposable"}""", Encoding.UTF8,
                                                               "application/json"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createBody = await created.Content.ReadFromJsonAsync<JsonElement>();
        var name       = createBody.GetProperty("name").GetString();
        Assert.NotNull(name);

        var deleted = await client.DeleteAsync($"/v1/{name}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        var deleteBody = await deleted.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.String, deleteBody.GetProperty("delete_time").ValueKind);

        var undeleted = await client.PostAsync($"/v1/{name}:undelete",
                                               new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, undeleted.StatusCode);
        var undeleteBody = await undeleted.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(!undeleteBody.TryGetProperty("delete_time", out var restoredDeleteTime)
                 || restoredDeleteTime.ValueKind == JsonValueKind.Null);

        var deletedAgain = await client.DeleteAsync($"/v1/{name}");
        Assert.Equal(HttpStatusCode.OK, deletedAgain.StatusCode);

        var expunged = await client.PostAsync($"/v1/{name}:expunge",
                                              new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, expunged.StatusCode);

        var fetched = await client.GetAsync($"/v1/{name}");
        Assert.Equal(HttpStatusCode.NotFound, fetched.StatusCode);
    }
}
