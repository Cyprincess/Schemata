using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Schemata.Resource.Http.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Http.Integration.Tests;

[Trait("Category", "Integration")]
public class ResourceHttpFilterIntegrationShould : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public ResourceHttpFilterIntegrationShould(WebAppFactory factory) { _factory = factory; }

    private static async Task SeedAsync(HttpClient client) {
        await client.PostAsJsonAsync("/v1/students", new Student { FullName = "Alice", Age = 18 });
        await client.PostAsJsonAsync("/v1/students", new Student { FullName = "Bob", Age   = 25 });
        await client.PostAsJsonAsync("/v1/students", new Student { FullName = "Carol", Age = 9 });
    }

    private static List<JsonElement> GetStudents(JsonElement body) {
        // Resource list responses use the pluralized, lowercase collection name.
        if (body.TryGetProperty("students", out var arr)) {
            var list = new List<JsonElement>();
            foreach (var elem in arr.EnumerateArray()) {
                list.Add(elem);
            }

            return list;
        }

        return [];
    }

    [Fact]
    public async Task Filter_ByAge_ReturnsOnlyMatchingStudents() {
        var client = _factory.CreateClient();
        await SeedAsync(client);

        var response = await client.GetAsync("/v1/students?filter=age%20%3C%2020");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body     = await response.Content.ReadFromJsonAsync<JsonElement>();
        var students = GetStudents(body);

        Assert.NotEmpty(students);
        foreach (var s in students) {
            if (s.TryGetProperty("age", out var ageProp) || s.TryGetProperty("Age", out ageProp)) {
                Assert.True(ageProp.GetInt32() < 20);
            }
        }
    }

    [Fact]
    public async Task PageSize_LimitsResults() {
        var client = _factory.CreateClient();
        await SeedAsync(client);

        var response = await client.GetAsync("/v1/students?PageSize=2");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body     = await response.Content.ReadFromJsonAsync<JsonElement>();
        var students = GetStudents(body);

        Assert.Equal(2, students.Count);

        var hasToken = body.TryGetProperty("next_page_token", out var tokenProp)
                    || body.TryGetProperty("nextPageToken", out tokenProp);
        Assert.True(hasToken, "Response should contain a next_page_token");
        Assert.False(string.IsNullOrWhiteSpace(tokenProp.GetString()), "next_page_token should be non-empty");
    }

    [Fact]
    public async Task ReadMask_TrimsUnlistedFields() {
        var client = _factory.CreateClient();
        await SeedAsync(client);

        var response = await client.GetAsync("/v1/students?ReadMask=age");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body     = await response.Content.ReadFromJsonAsync<JsonElement>();
        var students = GetStudents(body);

        Assert.NotEmpty(students);
        foreach (var student in students) {
            Assert.True(student.TryGetProperty("age", out var _));
            var hasName = student.TryGetProperty("full_name", out var fullName)
                       && fullName.ValueKind != JsonValueKind.Null;
            Assert.False(hasName);
        }
    }

    [Fact]
    public async Task ReadMask_UnknownPath_Returns422() {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/v1/students?ReadMask=no_such_field");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
