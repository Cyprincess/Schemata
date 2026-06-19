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
        var client   = _factory.CreateClient();
        var response = await client.GetAsync("/v1/students");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Post_NewStudent_Returns201() {
        var client   = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/v1/students", new Student { FullName = "Test" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Post_NewStudent_ResponseBodyContainsStudent() {
        var client   = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/v1/students", new Student { FullName = "Returned" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json    = await response.Content.ReadFromJsonAsync<JsonElement>();
        var hasName = json.TryGetProperty("name", out var nameProp) || json.TryGetProperty("Name", out nameProp);

        Assert.True(hasName, "Response should contain a 'name' property");
        Assert.False(string.IsNullOrWhiteSpace(nameProp.GetString()), "Name should be non-empty");
    }

    [Fact]
    public async Task Delete_ExistingStudent_Returns204() {
        var client  = _factory.CreateClient();
        var created = await client.PostAsJsonAsync("/v1/students", new Student { FullName = "ToDelete" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var body    = await created.Content.ReadFromJsonAsync<JsonElement>();
        var gotName = body.TryGetProperty("name", out var nameProp) || body.TryGetProperty("Name", out nameProp);
        Assert.True(gotName);

        var name     = nameProp.GetString()!;
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

        var response = await client.GetAsync($"/v1/{body!.Name}:preview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Previewable", preview.GetProperty("full_name").GetString());
    }

    [Fact]
    public async Task GetCustomMethod_PostVerb_IsRejected() {
        var client  = _factory.CreateClient();
        var created = await client.PostAsJsonAsync("/v1/students", new Student { FullName = "PostRejected" });
        var body    = await created.Content.ReadFromJsonAsync<Student>();

        var response = await client.PostAsJsonAsync($"/v1/{body!.Name}:preview", new Student());

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
        var name       = createBody.GetProperty("name").GetString()!;

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
