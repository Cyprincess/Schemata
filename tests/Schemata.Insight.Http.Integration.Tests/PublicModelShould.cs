using System;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Insight.Skeleton.Queries;
using Schemata.Security.Skeleton;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Schemata.Insight.Http.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Insight.Http.Integration.Tests;

[Trait("Category", "Integration")]
public class PublicModelShould(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    [Fact]
    public async Task EntityEntitlement_RemainsBeforePublicProjection() {
        using var host = factory.WithServices(s => s.AddSingleton<IEntitlementProvider<Student, QueryInsightRequest>, AdultStudents>());
        using var client = host.CreateClient();
        using var response = await client.PostAsJsonAsync("/v1/insight:query", new { sources = new[] { new { alias = "s", name = "students" } } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("rows").GetArrayLength());
        Assert.DoesNotContain(body.GetProperty("rows").EnumerateArray(), row => row.GetProperty("full_name").GetString() == "Bob");
    }

    private sealed class AdultStudents : IEntitlementProvider<Student, QueryInsightRequest> {
        public Task<Expression<Func<Student, bool>>?> GenerateEntitlementExpressionAsync(AccessContext<QueryInsightRequest> context, ClaimsPrincipal? principal, CancellationToken ct = default)
            => Task.FromResult<Expression<Func<Student, bool>>?>(student => student.Age >= 20);
    }

    [Theory]
    [InlineData("\"transformations\":[{\"order_by\":{\"order_by\":\"secret\"}}]")]
    [InlineData("\"transformations\":[{\"group_by\":{\"keys\":[\"p.secret\"],\"aggregations\":[]}}]")]
    [InlineData("\"transformations\":[{\"group_by\":{\"keys\":[],\"aggregations\":[{\"field\":\"p.secret\",\"function\":1,\"alias\":\"total\"}]}}]")]
    [InlineData("\"selections\":[{\"field\":\"p.children\",\"transformations\":[{\"filter\":{\"predicate\":{\"source\":\"has(c.detail.secret)\",\"language\":\"cel\"}}}],\"selections\":[{\"field\":\"c.number\"}]}]")]
    [InlineData("\"selections\":[{\"field\":\"p.children\",\"transformations\":[{\"order_by\":{\"order_by\":\"c.detail.secret\"}}],\"selections\":[{\"field\":\"c.number\"}]}]")]
    [InlineData("\"transformations\":[{\"compute\":{\"fields\":[{\"alias\":\"m\",\"expression\":{\"source\":\"{1:p.children[0].detail,'1':p.children[0]}\",\"language\":\"cel\"}}]}},{\"filter\":{\"predicate\":{\"source\":\"has(m[1].secret)\",\"language\":\"cel\"}}}]")]
    [InlineData("\"selections\":[{\"alias\":\"leak\",\"expression\":{\"source\":\"p.secret\",\"language\":\"cel\"}}]")]
    [InlineData("\"transformations\":[{\"compute\":{\"fields\":[{\"alias\":\"copy\",\"expression\":{\"source\":\"p.children[0].detail\",\"language\":\"cel\"}}]}},{\"filter\":{\"predicate\":{\"source\":\"has(copy.secret)\",\"language\":\"cel\"}}}]")]
    [InlineData("\"selections\":[{\"field\":\"p.children\",\"selections\":[{\"field\":\"c.detail.secret\"}]}]")]
    public async Task RejectHiddenField_InEveryLocalStage(string fragment) {
        using var client = factory.CreateClient();
        var request = "{\"sources\":[{\"alias\":\"p\",\"name\":\"protected\"}]," + fragment + "}";
        using var response = await client.PostAsync("/v1/insight:query", new System.Net.Http.StringContent(request, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("public query model", body);
    }

    [Fact]
    public async Task RejectHiddenField_OnEitherJoinOperand() {
        using var client = factory.CreateClient();
        foreach (var expression in new[] { "p.secret == b.id", "b.id == p.secret" }) {
            using var response = await client.PostAsJsonAsync("/v1/insight:query", new {
                sources = new[] { new { alias = "p", name = "protected" }, new { alias = "b", name = "buyers" } },
                joins = new[] { new { left = "p", right = "b", kind = 1, on = new { source = expression, language = "cel" } } },
            });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("public query model", await response.Content.ReadAsStringAsync());
        }
    }

    [Theory]
    [InlineData("secret > 0", "aip")]
    [InlineData("years = Secret", "aip")]
    [InlineData("years = protectedQuery.secret", "aip")]
    [InlineData("has(secret)", "cel")]
    [InlineData("string == 'Ada'", "cel")]
    [InlineData("children.map(x, x.detail).exists(y, has(y.secret))", "cel")]
    [InlineData("children.exists(x, has(x.detail.secret))", "cel")]
    public async Task RejectHiddenField_BeforeTypedFilterEvaluation(string expression, string language) {
        using var client = factory.CreateClient();
        var request = JsonSerializer.Serialize(new {
            sources = new[] { new { alias = "p", name = "protected" } },
            transformations = new[] { new { filter = new { predicate = new { source = expression, language } } } },
        });
        using var response = await client.PostAsync("/v1/insight:query", new System.Net.Http.StringContent(request, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("p.secret")]
    [InlineData("p.full_name")]
    [InlineData("p.children.0.detail.secret")]
    public async Task RejectHiddenOrEntityOnlySelection(string field) {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/v1/insight:query", new {
            sources = new[] { new { alias = "p", name = "protected" } },
            selections = new[] { new { field } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DefaultProjection_RecursivelyOmitsIgnoredMembers() {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/v1/insight:query", new { sources = new[] { new { alias = "p", name = "protected" } } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var row = body.GetProperty("rows")[0];
        Assert.Equal("Ada", row.GetProperty("label").GetString());
        Assert.False(row.TryGetProperty("secret", out _));
        Assert.False(row.TryGetProperty("full_name", out _));
        foreach (var child in row.GetProperty("children").EnumerateArray()) {
            Assert.False(child.GetProperty("detail").TryGetProperty("secret", out _));
            Assert.True(child.GetProperty("detail").TryGetProperty("label", out _));
        }
    }

    [Trait("Layer", "Integration")]
    [Theory]
    [InlineData("p.children[0].detail.label")]
    [InlineData("[p.children[0].detail, p.children[1].detail][0].label")]
    [InlineData("{'picked':p.children[0].detail}['picked'].label")]
    [InlineData("p.children.map(x, x.detail)[0].label")]
    [InlineData("dyn(p.children[0].detail).label")]
    [InlineData("(p.children + [p.children[0]])[0].detail.label")]
    [InlineData("{1:p.children[0].detail, '1':p.children[1].detail}[1].label")]
    public async Task AllowPublicStructuralExpression(string expression) {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/v1/insight:query", new {
            sources = new[] { new { alias = "p", name = "protected" } },
            selections = new[] { new { alias = "value", expression = new { source = expression, language = "cel" } } },
        });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("paid", Schemata.Common.ScalarPayloadConverter.ReadValue(body.GetProperty("rows")[0].GetProperty("value")));
    }
}
