using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Schemata.Abstractions;
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

    [Theory]
    [InlineData("tags == other_tags")]
    [InlineData("tags == ['a']")]
    public async Task Cel_Collection_Equality_Filters_SQLite_Rows_Locally(string equality) {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient();
        foreach (var (name, age, tags) in new[] { ("match", 20, new[] { "a" }), ("different", 20, new[] { "b" }), ("young", 10, new[] { "a" }) }) {
            using var created = await client.PostAsJsonAsync("/v1/students", new {
                full_name = name, age, tags, other_tags = new[] { "a" },
            });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }
        using var response = await client.GetAsync("/v1/students?language=cel&filter=" + Uri.EscapeDataString("age > 18 && " + equality));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("match", Assert.Single(GetStudents(body)).GetProperty("full_name").GetString());
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
    public async Task Filter_IncompatibleLiteral_ReturnsInvalidFilter() {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(
            "/v1/students?filter=" + Uri.EscapeDataString("age = 'hello'"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var violation = Assert.Single(body.GetProperty("error").GetProperty("details").EnumerateArray()
                                            .Where(d => d.TryGetProperty("field_violations", out _))
                                            .SelectMany(d => d.GetProperty("field_violations").EnumerateArray()));
        Assert.Equal("filter", violation.GetProperty("field").GetString());
        Assert.Equal(SchemataResources.INVALID_FILTER, violation.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task PageSize_LimitsResults() {
        var client = _factory.CreateClient();
        await SeedAsync(client);

        var response = await client.GetAsync("/v1/students?page_size=2");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body     = await response.Content.ReadFromJsonAsync<JsonElement>();
        var students = GetStudents(body);

        Assert.Equal(2, students.Count);

        var hasToken = body.TryGetProperty("next_page_token", out var tokenProp)
                    || body.TryGetProperty("nextPageToken", out tokenProp);
        Assert.True(hasToken, "Response should contain a next_page_token");
        Assert.False(string.IsNullOrWhiteSpace(tokenProp.GetString()), "next_page_token should be non-empty");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task List_BindsSnakeCaseOrderingAndDeletedVisibility(bool showDeleted) {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient();
        var fullName = $"BindingVisibility{showDeleted}";
        foreach (var age in new[] { 10, 30, 20 }) {
            var created = await client.PostAsync("/v1/trashes",
                new StringContent(JsonSerializer.Serialize(new { full_name = fullName, age }), Encoding.UTF8,
                                  "application/json"));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var entity = await created.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(fullName, entity.GetProperty("full_name").GetString());
            if (age == 30) {
                var deleted = await client.DeleteAsync("/v1/" + entity.GetProperty("name").GetString());
                Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
            }
        }

        var query = "/v1/trashes?filter=" + Uri.EscapeDataString($"full_name=\"{fullName}\"")
                  + "&order_by=age%20desc&show_deleted=" + showDeleted.ToString().ToLowerInvariant();
        var response = await client.GetAsync(query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(showDeleted ? new[] { 30, 20, 10 } : new[] { 20, 10 },
                     body.GetProperty("trashes").EnumerateArray().Select(entity => entity.GetProperty("age").GetInt32()));
    }

}
