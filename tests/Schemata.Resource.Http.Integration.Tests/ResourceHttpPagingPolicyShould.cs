using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core.Building;
using Schemata.Resource.Http.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Http.Integration.Tests;

/// <summary>
///     Behavioral coverage for the configurable List paging policy (default/maximum page
///     size, precedence, request coercion, and continuation size changes), per
///     <seealso href="https://google.aip.dev/158">AIP-158: Pagination</seealso>.
/// </summary>
[Trait("Category", "Integration")]
public class ResourceHttpPagingPolicyShould : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public ResourceHttpPagingPolicyShould(WebAppFactory factory) { _factory = factory; }

    private static async Task CreateStudentsAsync(HttpClient client, string prefix, int count) {
        for (var i = 0; i < count; i++) {
            var created = await client.PostAsync("/v1/students",
                new StringContent(JsonSerializer.Serialize(new { full_name = $"{prefix}{i:d2}" }), Encoding.UTF8,
                                  "application/json"));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }
    }

    private static string Query(string prefix, string? pageSize = null, string? token = null) {
        var query = "/v1/students?filter=" + Uri.EscapeDataString($"full_name:{JsonSerializer.Serialize(prefix)}") + "&order_by=uid asc";
        if (pageSize is not null) {
            query += "&page_size=" + pageSize;
        }

        if (token is not null) {
            query += "&page_token=" + Uri.EscapeDataString(token);
        }

        return query;
    }

    private static async Task<JsonElement> GetPageAsync(HttpClient client, string query) {
        var response = await client.GetAsync(query);
        Assert.True(HttpStatusCode.OK == response.StatusCode,
            $"status={response.StatusCode} body={await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private WebApplicationFactory<Program> WithPolicy(int defaultSize, int maxSize) {
        return _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.PostConfigure<SchemataResourceOptions>(
                options => {
                    options.DefaultPageSize = defaultSize;
                    options.MaxPageSize    = maxSize;
                })));
    }

    [Fact]
    public async Task Get_OmittedPageSize_UsesConfiguredDefault() {
        using var factory = WithPolicy(3, 10);
        using var client  = factory.CreateClient();
        await CreateStudentsAsync(client, "PageDefault", 5);

        var page = await GetPageAsync(client, Query("PageDefault"));

        Assert.Equal(3, page.GetProperty("students").GetArrayLength());
        Assert.False(string.IsNullOrEmpty(page.GetProperty("next_page_token").GetString()));
    }

    [Fact]
    public async Task Get_ZeroPageSize_UsesConfiguredDefault() {
        using var factory = WithPolicy(3, 10);
        using var client  = factory.CreateClient();
        await CreateStudentsAsync(client, "PageZero", 5);

        var page = await GetPageAsync(client, Query("PageZero", "0"));

        Assert.Equal(3, page.GetProperty("students").GetArrayLength());
    }

    [Fact]
    public async Task Get_NegativePageSize_ReturnsInvalidArgument() {
        using var client = _factory.CreateClient();
        await CreateStudentsAsync(client, "PageNegative", 1);

        var response = await client.GetAsync(Query("PageNegative", "-1"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body     = await response.Content.ReadFromJsonAsync<JsonElement>();
        var details  = body.GetProperty("error").GetProperty("details");
        var violation = Assert.Single(details.EnumerateArray()
                                           .Where(d => d.TryGetProperty("field_violations", out _))
                                           .SelectMany(d => d.GetProperty("field_violations").EnumerateArray()));
        Assert.Equal("page_size", violation.GetProperty("field").GetString());
    }

    [Fact]
    public async Task Get_ContinuationWithSmallerPageSize_ResumesWithoutGapsOrDuplicates() {
        using var factory = WithPolicy(5, 500);
        using var client  = factory.CreateClient();
        await CreateStudentsAsync(client, "PageShrink", 14);

        var first = await GetPageAsync(client, Query("PageShrink", "12"));
        Assert.Equal(12, first.GetProperty("students").GetArrayLength());

        var token = first.GetProperty("next_page_token").GetString();
        Assert.False(string.IsNullOrEmpty(token));

        var second = await GetPageAsync(client, Query("PageShrink", "2", token));
        Assert.False(second.TryGetProperty("next_page_token", out _));

        var firstUids = first.GetProperty("students").EnumerateArray()
                             .Select(s => s.GetProperty("uid").GetString()).ToHashSet();
        var secondUids = second.GetProperty("students").EnumerateArray()
                                .Select(s => s.GetProperty("uid").GetString()).ToList();
        Assert.Equal(2, secondUids.Count);
        Assert.DoesNotContain(secondUids, firstUids.Contains);
    }

    [Fact]
    public async Task Get_PerResourceAttribute_OverridesGlobalPolicy() {
        using var client = _factory.CreateClient();
        for (var i = 0; i < 5; i++) {
            var created = await client.PostAsync("/v1/pagedThings",
                new StringContent(JsonSerializer.Serialize(new { label = $"Policy{i:d2}" }), Encoding.UTF8,
                                  "application/json"));
            Assert.True(HttpStatusCode.Created == created.StatusCode,
                $"status={created.StatusCode} body={await created.Content.ReadAsStringAsync()}");
        }

        var query = "/v1/pagedThings?filter=" + Uri.EscapeDataString("label:\"Policy\"") + "&order_by=uid asc";

        var omitted = await GetPageAsync(client, query);
        Assert.Equal(2, omitted.GetProperty("paged_things").GetArrayLength());

        var zero = await GetPageAsync(client, query + "&page_size=0");
        Assert.Equal(2, zero.GetProperty("paged_things").GetArrayLength());

        var oversize = await GetPageAsync(client, query + "&page_size=1000");
        Assert.Equal(3, oversize.GetProperty("paged_things").GetArrayLength());
    }

    [Fact]
    public async Task Get_OversizePageSize_CoercesToConfiguredMaximum() {
        using var factory = WithPolicy(3, 4);
        using var client  = factory.CreateClient();
        await CreateStudentsAsync(client, "PageOversize", 6);

        var page = await GetPageAsync(client, Query("PageOversize", "1000"));

        Assert.Equal(4, page.GetProperty("students").GetArrayLength());
        Assert.False(string.IsNullOrEmpty(page.GetProperty("next_page_token").GetString()));
    }

    [Fact]
    public async Task Get_InvalidConfiguredPolicy_FailsClosed() {
        using var factory = WithPolicy(10, 3);
        using var client  = factory.CreateClient();
        await CreateStudentsAsync(client, "PageInvalid", 1);

        var response = await client.GetAsync(Query("PageInvalid"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }
}
