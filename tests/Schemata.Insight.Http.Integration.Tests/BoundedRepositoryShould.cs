using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Insight.Foundation;
using Schemata.Insight.Http.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Insight.Http.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Integration")]
public sealed class BoundedRepositoryShould : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public BoundedRepositoryShould(WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Aliased_Source_Field_And_Nested_Aggregate_Use_Their_Selected_Types() {
        using var client = _factory.CreateClient();
        using var aliased = await client.PostAsJsonAsync("/v1/insight:query", new {
            sources = new[] { new { alias = "s", name = "students" } },
            selections = new object[] { new { field = "s.age", alias = "full_name" },
                new { alias = "computed", expression = new { source = "s.age + 1", language = "cel" } } },
        });
        Assert.Equal(HttpStatusCode.OK, aliased.StatusCode);
        var projected = await aliased.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("int64", System.Linq.Enumerable.Single(projected.GetProperty("schema").EnumerateArray(),
            field => field.GetProperty("name").GetString() == "full_name").GetProperty("type").GetString());
        Assert.Equal(new[] { 19, 24, 36 }, System.Linq.Enumerable.OrderBy(
            System.Linq.Enumerable.Select(projected.GetProperty("rows").EnumerateArray(), row => row.GetProperty("full_name").GetInt32()), value => value));
        using var nested = await client.PostAsJsonAsync("/v1/insight:query", new {
            sources = new[] { new { alias = "c", name = "customers" } },
            selections = new[] { new { field = "c.orders", alias = "orders",
                transformations = new object[] {
                    new { group_by = new { keys = new[] { "o.status" }, aggregations = new[] { new { field = "o.amount", function = "sum", alias = "amount" } } } },
                    new { filter = new { predicate = new { source = "o.amount > 0.0", language = "cel" } } },
                    new { order_by = new { order_by = "o.amount desc" } },
                    new { compute = new { fields = new[] { new { alias = "doubled", expression = new { source = "o.amount * 2.0", language = "cel" } } } } },
                }, selections = new object[] { new { field = "o.amount" }, new { field = "doubled" },
                    new { alias = "observed", expression = new { source = "o.amount + 1.0", language = "cel" } } } } },
        });
        Assert.Equal(HttpStatusCode.OK, nested.StatusCode);
        var grouped = await nested.Content.ReadFromJsonAsync<JsonElement>();
        var descriptor = System.Linq.Enumerable.Single(grouped.GetProperty("schema").EnumerateArray());
        Assert.Equal("double", System.Linq.Enumerable.Single(descriptor.GetProperty("children").EnumerateArray(),
            field => field.GetProperty("name").GetString() == "amount").GetProperty("type").GetString());
        Assert.Contains(System.Linq.Enumerable.SelectMany(grouped.GetProperty("rows").EnumerateArray(),
            row => row.GetProperty("orders").EnumerateArray()), row => row.GetProperty("amount").GetDouble() == 350d);
        Assert.Contains(System.Linq.Enumerable.SelectMany(grouped.GetProperty("rows").EnumerateArray(),
            row => row.GetProperty("orders").EnumerateArray()), row =>
                Equals(700d, Schemata.Common.ScalarPayloadConverter.ReadValue(row.GetProperty("doubled")))
                && Equals(351d, Schemata.Common.ScalarPayloadConverter.ReadValue(row.GetProperty("observed"))));
    }

    [Theory]
    [InlineData("^Nobody$")]
    [InlineData("^Cleo$")]
    public async Task Rejects_Residual_Supersets_That_Exceed_The_Raw_Read_Budget(string pattern) {
        using var factory = _factory.WithServices(services => services.Configure<SchemataInsightOptions>(options => options.MaxResidualScanRows = 2));
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/v1/insight:query", new {
            sources = new[] { new { alias = "s", name = "students" } },
            language = "cel",
            transformations = new[] { new { filter = new { predicate = new { source = $"full_name.matches('{pattern}')", language = "cel" } } } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("INVALID_ARGUMENT", error.GetProperty("error").GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("^Nobody$", 0)]
    [InlineData("^Cleo$", 1)]
    public async Task Completes_Zero_And_Selective_Residuals_At_The_Exact_Raw_Budget(string pattern, int expected) {
        using var factory = _factory.WithServices(services => services.Configure<SchemataInsightOptions>(options => options.MaxResidualScanRows = 3));
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/v1/insight:query", new {
            sources = new[] { new { alias = "s", name = "students" } },
            language = "cel",
            transformations = new[] { new { filter = new { predicate = new { source = $"full_name.matches('{pattern}')", language = "cel" } } } },
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expected, body.GetProperty("total_size").GetInt32());
        if (expected == 0) Assert.Empty(body.GetProperty("rows").EnumerateArray());
        else Assert.Equal("Cleo", Assert.Single(body.GetProperty("rows").EnumerateArray()).GetProperty("full_name").GetString());
    }

    [Fact]
    public async Task Continues_Real_Repository_Pages_Without_Duplicates_Or_Omissions() {
        using var client = _factory.CreateClient();
        string? token = null;
        var names = new List<string?>();
        do {
            using var response = await client.PostAsJsonAsync("/v1/insight:query", new {
                sources = new[] { new { alias = "s", name = "students" } },
                transformations = new[] { new { order_by = new { order_by = "age" } } },
                page_size = 1,
                page_token = token,
            });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            foreach (var row in body.GetProperty("rows").EnumerateArray()) names.Add(row.GetProperty("full_name").GetString());
            token = body.TryGetProperty("next_page_token", out var next) ? next.GetString() : null;
        } while (token is not null);
        Assert.Equal(new[] { "Bob", "Cleo", "Ada" }, names);
    }
}
