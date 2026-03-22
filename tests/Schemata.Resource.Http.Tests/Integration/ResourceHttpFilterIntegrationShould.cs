using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Schemata.Resource.Http.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Http.Tests.Integration;

[Trait("Category", "Integration")]
public class ResourceHttpFilterIntegrationShould : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public ResourceHttpFilterIntegrationShould(WebAppFactory factory) { _factory = factory; }

    private static async Task SeedAsync(HttpClient client) {
        await client.PostAsJsonAsync("/students", new Student { FullName = "Alice", Age = 18 });
        await client.PostAsJsonAsync("/students", new Student { FullName = "Bob", Age   = 25 });
        await client.PostAsJsonAsync("/students", new Student { FullName = "Carol", Age = 9 });
    }

    private static List<JsonElement> GetStudents(JsonElement body) {
        // The JSON uses "students" (pluralized, lowercase) instead of "entities"
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

        // filter=age < 20 (URL encoded; field names are snake_case)
        var response = await client.GetAsync("/students?filter=age%20%3C%2020");
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

        var response = await client.GetAsync("/students?PageSize=2");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body     = await response.Content.ReadFromJsonAsync<JsonElement>();
        var students = GetStudents(body);

        Assert.Equal(2, students.Count);

        // next_page_token should be present when results were limited
        var hasToken = body.TryGetProperty("next_page_token", out var tokenProp)
                    || body.TryGetProperty("nextPageToken", out tokenProp);
        Assert.True(hasToken, "Response should contain a next_page_token");
        Assert.False(string.IsNullOrWhiteSpace(tokenProp.GetString()), "next_page_token should be non-empty");
    }
}
