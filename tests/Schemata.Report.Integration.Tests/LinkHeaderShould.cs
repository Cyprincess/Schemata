using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Schemata.Report.Integration.Tests.Fixtures;
using Schemata.Abstractions.Linking;
using Schemata.Transport.Http.Linking;
using Xunit;

namespace Schemata.Report.Integration.Tests;

[Trait("Category", "Integration")]
public class LinkHeaderShould : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public LinkHeaderShould(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Definition_Emits_Snapshot_List_And_Schedule_Links() {
        using var client = _factory.CreateClient();
        var name = await CreateAsync(client, true);

        var response = await client.GetAsync($"/v1/reports/{name}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var links     = Links(response);
        var snapshots = Assert.Single(links, link => link.Rel == "related"
                                                  && Target(link).EndsWith($"/v1/reports/{name}/snapshots"));
        Assert.EndsWith($"/v1/reports/{name}/snapshots", Target(snapshots));

        var schedule = Assert.Single(links, link => link.Rel == "related" && link.Title == "schedule");
        Assert.Contains("/v1/jobs", schedule.Target);
        // The slot key factory is asserted directly: this host enables no resource expression
        // language, so following the filtered List would be rejected before it can resolve rows.
        Assert.Equal($"key=\"report:reports/{name}\"", Filter(schedule));
    }

    [Fact]
    public async Task Non_Periodic_Definition_Has_No_Schedule_Link() {
        using var client = _factory.CreateClient();
        var name = await CreateAsync(client, false);

        var response = await client.GetAsync($"/v1/reports/{name}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var links = Links(response);
        Assert.DoesNotContain(links, link => link.Title == "schedule");
        Assert.DoesNotContain(links, link => link.Target.Contains("/v1/jobs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Snapshot_Emits_Definition_And_Row_Read_Links() {
        using var client = _factory.CreateClient();
        var report = await CreateAsync(client, false);

        var generated = await client.PostAsync("/v1/reports:generate",
            new StringContent($$"""{"name":"{{report}}","persist":true,"sync":true}""",
                              Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, generated.StatusCode);

        var listed = await client.GetFromJsonAsync<JsonElement>($"/v1/reports/{report}/snapshots");
        var snapshot = listed.GetProperty("snapshots")[0].GetProperty("name").GetString();
        Assert.NotNull(snapshot);

        var response = await client.GetAsync("/v1/" + snapshot);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var links      = Links(response);
        var definition = Assert.Single(links, link => link.Rel == "up" && link.Title == "report");
        Assert.EndsWith($"/v1/reports/{report}", Target(definition));

        var rows = Assert.Single(links, link => link.Rel == "related" && link.Title == "rows");
        Assert.EndsWith($"/v1/{snapshot}:read", Target(rows));

        var read = await client.GetAsync(rows.Target);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(3, (await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rows").GetArrayLength());
    }

    private static async Task<string> CreateAsync(HttpClient client, bool periodic) {
        var definition = JsonSerializer.Serialize(new {
            sources    = new[] { new { alias = "record", name = "source-records" } },
            selections = new[] { new { field = "value" } },
        });
        var response = await client.PostAsJsonAsync("/v1/reports", new {
            definition,
            source_kind   = 0,
            periodic,
            interval_ticks = periodic ? TimeSpan.FromDays(1).Ticks : (long?)null,
            schedule_kind = 0,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        return created.GetProperty("name").GetString()!.Split('/').Last();
    }

    private static IReadOnlyList<WebLink> Links(HttpResponseMessage response) {
        var values = response.Headers.GetValues(LinkHeader.Name).ToList();
        Assert.Single(values);
        var links = LinkHeader.Parse(values[0], response.RequestMessage!.RequestUri);
        Assert.Equal(links.Count, links.Select(Key).Distinct().Count());
        return links;
    }

    private static string Key(WebLink link) => string.Join("|",
        link.Target, link.Rel, link.Anchor, link.Hreflang, link.Media, link.Title, link.TitleStar, link.Type,
        link.Extensions is null
            ? string.Empty
            : string.Join(";", link.Extensions.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                                              .Select(pair => pair.Key + "=" + pair.Value)));

    private static string Target(WebLink link) => Uri.UnescapeDataString(new Uri(link.Target).AbsoluteUri);

    private static string? Filter(WebLink link) =>
        QueryHelpers.ParseQuery(new Uri(link.Target).Query)["filter"].ToString();
}
