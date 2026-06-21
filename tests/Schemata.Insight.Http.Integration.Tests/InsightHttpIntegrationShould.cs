using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Schemata.Insight.Http.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Insight.Http.Integration.Tests;

[Trait("Category", "Integration")]
public class InsightHttpIntegrationShould : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public InsightHttpIntegrationShould(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Query_AllStudents_Returns200WithRowsAndTotal() {
        var client = _factory.CreateClient();

        var response = await Query(client, """{"sources":[{"alias":"s","name":"students"}]}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, body.GetProperty("rows").GetArrayLength());
        Assert.Equal(3, body.GetProperty("total_size").GetInt32());
    }

    [Fact]
    public async Task Query_FilterOrderAndPage_ProjectsAndPaginates() {
        var client = _factory.CreateClient();

        var request = """
        {
            "sources": [{ "alias": "s", "name": "students" }],
            "transformations": [
                { "filter": { "predicate": { "source": "age > 20" } } },
                { "order_by": { "order_by": "age desc" } }
            ],
            "selections": [{ "field": "s.full_name" }],
            "page_size": 1
        }
        """;

        var response = await Query(client, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var rows = body.GetProperty("rows");
        Assert.Equal(1, rows.GetArrayLength());
        Assert.Equal("Ada", rows[0].GetProperty("full_name").GetString());
        Assert.Equal(2, body.GetProperty("total_size").GetInt32());
        Assert.Equal(JsonValueKind.String, body.GetProperty("next_page_token").ValueKind);
    }

    [Fact]
    public async Task Query_UnknownSource_Returns404() {
        var client = _factory.CreateClient();

        var response = await Query(client, """{"sources":[{"alias":"x","name":"missing"}]}""");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Query_JoinWithUnknownAlias_Returns400() {
        var client = _factory.CreateClient();

        var request = """
        {
            "sources": [{ "alias": "b", "name": "buyers" }, { "alias": "p", "name": "purchases" }],
            "joins": [{ "left": "b", "right": "x", "kind": 1, "on": { "source": "b.id == p.buyer_id", "language": "cel" } }]
        }
        """;

        var response = await Query(client, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Query_NestedChildList_ProjectsFilteredOrderedToppedComputedChildren() {
        var client = _factory.CreateClient();

        var request = """
        {
            "sources": [{ "alias": "c", "name": "customers" }],
            "selections": [
                { "field": "c.full_name", "alias": "full_name" },
                {
                    "field": "c.orders",
                    "alias": "recent_paid_orders",
                    "transformations": [
                        { "filter": { "predicate": { "source": "o.status = 'paid'" } } },
                        { "order_by": { "order_by": "o.placed desc" } },
                        { "top": { "count": 2 } }
                    ],
                    "selections": [
                        { "field": "o.number", "alias": "number" },
                        { "expression": { "source": "o.amount * 1.1", "language": "cel" }, "alias": "total" }
                    ]
                }
            ]
        }
        """;

        var response = await Query(client, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var row    = body.GetProperty("rows")[0];
        Assert.Equal("Ada", row.GetProperty("full_name").GetString());

        var orders = row.GetProperty("recent_paid_orders");
        Assert.Equal(2, orders.GetArrayLength());
        Assert.Equal(3, orders[0].GetProperty("number").GetInt32());
        Assert.Equal(220d, orders[0].GetProperty("total").GetDouble(), 6);
        Assert.Equal(1, orders[1].GetProperty("number").GetInt32());
        Assert.Equal(110d, orders[1].GetProperty("total").GetDouble(), 6);
    }

    [Fact]
    public async Task Query_TwoSourceJoin_ProjectsBothSidesFiltered() {
        var client = _factory.CreateClient();

        var request = """
        {
            "sources": [
                { "alias": "b", "name": "buyers" },
                { "alias": "p", "name": "purchases" }
            ],
            "joins": [
                { "left": "b", "right": "p", "kind": 1, "on": { "source": "b.id == p.buyer_id", "language": "cel" } }
            ],
            "transformations": [
                { "filter": { "predicate": { "source": "p.status == 'paid'", "language": "cel" } } }
            ],
            "selections": [
                { "field": "b.full_name", "alias": "full_name" },
                { "field": "p.amount", "alias": "amount" }
            ]
        }
        """;

        var response = await Query(client, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var rows = body.GetProperty("rows");

        Assert.Equal(2, rows.GetArrayLength());
        foreach (var row in rows.EnumerateArray()) {
            Assert.False(string.IsNullOrEmpty(row.GetProperty("full_name").GetString()));
            Assert.True(row.GetProperty("amount").GetInt32() > 0);
        }
        Assert.Contains(rows.EnumerateArray(), r => r.GetProperty("full_name").GetString() == "Bob" && r.GetProperty("amount").GetInt32() == 200);
    }

    [Fact]
    public async Task Query_RuntimeRegisteredSource_ResolvesThroughTheDatabaseCatalog() {
        var client = _factory.CreateClient();

        var response = await Query(client, """{"sources":[{"alias":"b","name":"live_buyers"}]}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("rows").GetArrayLength());
        Assert.Contains(body.GetProperty("rows").EnumerateArray(),
                        r => r.GetProperty("full_name").GetString() == "Ada");
    }

    private static Task<HttpResponseMessage> Query(HttpClient client, string json) {
        return client.PostAsync("/v1/insight:query", new StringContent(json, Encoding.UTF8, "application/json"));
    }
}
