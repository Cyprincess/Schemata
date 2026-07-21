using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Common;
using Schemata.Report.Integration.Tests.Fixtures;
using Schemata.Scheduling.Skeleton;
using Xunit;

namespace Schemata.Report.Integration.Tests;

[Trait("Category", "Integration")]
public class ReportHttpIntegrationShould : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public ReportHttpIntegrationShould(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Create_Generate_Sync_And_Read_Persisted_Expression_Report() {
        using var client = _factory.CreateClient();
        var report = await CreateReportAsync(client);

        var generated = await GenerateAsync(client, $$"""{ "name": "{{report}}", "persist": true, "sync": true }""");
        Assert.True(generated.IsSuccessStatusCode, await generated.Content.ReadAsStringAsync());
        var operation = await Json(generated);
        Assert.True(operation.GetProperty("done").GetBoolean());
        var operationName = operation.GetProperty("name").GetString()!;

        var found = await client.GetAsync("/v1/" + operationName);
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        var foundJson = await Json(found);
        Assert.True(foundJson.GetProperty("done").GetBoolean());

        var snapshots = await client.GetAsync($"/v1/reports/{report}/snapshots");
        Assert.Equal(HttpStatusCode.OK, snapshots.StatusCode);
        var snapshot = (await Json(snapshots)).GetProperty("snapshots")[0];
        Assert.Equal(operationName, snapshot.GetProperty("operation").GetString());
        var snapshotName = snapshot.GetProperty("name").GetString()!;

        var get = await client.GetAsync("/v1/" + snapshotName);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(operationName, (await Json(get)).GetProperty("operation").GetString());

        var rows = await ReadValuesAsync(client, snapshotName, 2);
        Assert.Equal([1, 2, 3], rows);

        Console.WriteLine($"POST /v1/reports:generate => {operation.GetRawText()}");
        Console.WriteLine($"GET /v1/{operationName} => {foundJson.GetRawText()}");
    }

    [Fact]
    public async Task Generate_Async_Exposes_Pending_Operation_And_Persists_Snapshot() {
        using var client = _factory.CreateClient();
        var report = await CreateReportAsync(client);

        var response = await GenerateAsync(client, $$"""{ "name": "{{report}}", "persist": true }""");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var pending = await Json(response);
        Assert.False(pending.GetProperty("done").GetBoolean());
        var operationName = pending.GetProperty("name").GetString()!;

        var complete = await WaitForDoneAsync(client, operationName);
        Assert.True(complete.GetProperty("done").GetBoolean());
        using var scope = _factory.Services.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<IOperationService>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var inProcess = await operations.WaitAsync(operationName, timeout.Token);
        Assert.True(inProcess.Done);
        var snapshots = await client.GetFromJsonAsync<JsonElement>($"/v1/reports/{report}/snapshots");
        Assert.True(snapshots.GetProperty("snapshots").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Reject_Generate_Request_With_Both_Name_And_Query() {
        using var client = _factory.CreateClient();
        var response = await GenerateAsync(client, """
            { "name": "dsl-records", "query": { "sources": [{ "alias": "record", "name": "source-records" }] }, "persist": true }
            """);

        Assert.True((int)response.StatusCode is >= 400 and < 500);
    }

    private static async Task<string> CreateReportAsync(HttpClient client) {
        var definition = JsonSerializer.Serialize(new {
            sources = new[] { new { alias = "record", name = "source-records" } },
            selections = new[] { new { field = "value" } },
        }, SchemataJson.Default);
        var response = await client.PostAsJsonAsync("/v1/reports", new {
            definition,
            source_kind = 0,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await Json(response);
        return created.GetProperty("name").GetString()!.Split('/').Last();
    }

    private static Task<HttpResponseMessage> GenerateAsync(HttpClient client, string body) {
        return client.PostAsync("/v1/reports:generate", new StringContent(body, Encoding.UTF8, "application/json"));
    }

    private static async Task<JsonElement> WaitForDoneAsync(HttpClient client, string operationName) {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true) {
            timeout.Token.ThrowIfCancellationRequested();
            var response = await client.GetAsync("/v1/" + operationName, timeout.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var operation = await Json(response);
            if (operation.GetProperty("done").GetBoolean()) {
                return operation;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
        }
    }

    private static async Task<List<int>> ReadValuesAsync(HttpClient client, string snapshotName, int pageSize) {
        var values = new List<int>();
        string? token = null;
        do {
            var suffix = token is null ? string.Empty : "&page_token=" + Uri.EscapeDataString(token);
            var response = await client.GetAsync($"/v1/{snapshotName}:read?page_size={pageSize}{suffix}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var page = await Json(response);
            values.AddRange(page.GetProperty("rows").EnumerateArray().Select(row => row.GetProperty("value").GetInt32()));
            token = page.TryGetProperty("next_page_token", out var next) ? next.GetString() : null;
            Console.WriteLine($"GET /v1/{snapshotName}:read?page_size={pageSize}{suffix} => {page.GetRawText()}");
        } while (token is not null);

        return values;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) {
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
