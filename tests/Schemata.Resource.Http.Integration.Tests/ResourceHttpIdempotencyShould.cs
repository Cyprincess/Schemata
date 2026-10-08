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

[Trait("Category", "Integration")]
public class ResourceHttpIdempotencyShould : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public ResourceHttpIdempotencyShould(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Post_DuplicateRequestId_CreatesOneOrder_AndReturnsTheFirstResponse() {
        var client    = _factory.CreateClient();
        var requestId = Guid.NewGuid().ToString();
        var note      = $"seq-{requestId}";

        var first  = await PostOrderAsync(client, note, requestId);
        var second = await PostOrderAsync(client, note, requestId);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var firstName  = await ReadNameAsync(first);
        var secondName = await ReadNameAsync(second);
        Assert.Equal(firstName, secondName);

        var listed = await ListOrdersAsync(client, note);
        Assert.Single(listed);
    }

    [Fact]
    public async Task Post_ConcurrentDuplicates_ResolveToASingleMutation() {
        var client    = _factory.CreateClient();
        var requestId = Guid.NewGuid().ToString();
        var note      = $"conc-{requestId}";

        var responses = await Task.WhenAll(Enumerable.Range(0, 4)
                                                     .Select(_ => PostOrderAsync(client, note, requestId)));

        foreach (var response in responses) {
            Assert.True(HttpStatusCode.Created == response.StatusCode,
                $"status={response.StatusCode} body={await response.Content.ReadAsStringAsync()}");
        }

        var names = await Task.WhenAll(responses.Select(ReadNameAsync));
        Assert.Single(names.Distinct());

        var listed = await ListOrdersAsync(client, note);
        Assert.Single(listed);
    }

    [Fact]
    public async Task Post_SameRequestIdWithDifferentBody_FailsValidation() {
        var client    = _factory.CreateClient();
        var requestId = Guid.NewGuid().ToString();

        var created = await PostOrderAsync(client, $"first-{requestId}", requestId);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var replay = await PostOrderAsync(client, $"changed-{requestId}", requestId);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, replay.StatusCode);
        var body = await replay.Content.ReadFromJsonAsync<JsonElement>();
        var violation = Assert.Single(body.GetProperty("error").GetProperty("details").EnumerateArray()
                                          .Where(d => d.TryGetProperty("field_violations", out _))
                                          .SelectMany(d => d.GetProperty("field_violations").EnumerateArray()));
        Assert.Equal("request_id", violation.GetProperty("field").GetString());
        Assert.Equal("REQUEST_ID_PAYLOAD_MISMATCH", violation.GetProperty("reason").GetString());

        var listed = await ListOrdersAsync(client, $"changed-{requestId}");
        Assert.Empty(listed);
    }

    [Fact]
    public async Task Post_ReplayAfterRetentionExpiry_ExecutesFresh() {
        using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.PostConfigure<SchemataResourceOptions>(options =>
                options.IdempotencyRetention = TimeSpan.FromMilliseconds(500))));
        using var client    = factory.CreateClient();
        var       requestId = Guid.NewGuid().ToString();
        var       note      = $"exp-{requestId}";

        var first = await PostOrderAsync(client, note, requestId);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        await Task.Delay(TimeSpan.FromSeconds(1.2));

        var replay = await PostOrderAsync(client, note, requestId);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);

        var firstName  = await ReadNameAsync(first);
        var replayName = await ReadNameAsync(replay);
        Assert.NotEqual(firstName, replayName);

        var listed = await ListOrdersAsync(client, note);
        Assert.Equal(2, listed.Length);
    }

    [Fact]
    public async Task Post_SameRequestIdAcrossTenants_ExecutesIndependently() {
        var client    = _factory.CreateClient();
        var requestId = Guid.NewGuid().ToString();
        var noteA     = $"tenant-a-{requestId}";
        var noteB     = $"tenant-b-{requestId}";

        var first  = await PostOrderAsync(client, noteA, requestId, Guid.NewGuid());
        var second = await PostOrderAsync(client, noteB, requestId, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var firstName  = await ReadNameAsync(first);
        var secondName = await ReadNameAsync(second);
        Assert.NotEqual(firstName, secondName);

        Assert.Single(await ListOrdersAsync(client, noteA));
        Assert.Single(await ListOrdersAsync(client, noteB));
    }

    private static async Task<HttpResponseMessage> PostOrderAsync(HttpClient client, string note, string requestId,
        Guid? tenant = null) {
        var json = JsonSerializer.Serialize(new { note, request_id = requestId });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/idempotentOrders") {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (tenant is not null) {
            request.Headers.Add("X-Test-Tenant", tenant.Value.ToString());
        }

        return await client.SendAsync(request);
    }

    private static async Task<string> ReadNameAsync(HttpResponseMessage response) {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("name").GetString()!;
    }

    private static async Task<JsonElement[]> ListOrdersAsync(HttpClient client, string note) {
        var response = await client.GetAsync("/v1/idempotentOrders?filter=" + Uri.EscapeDataString($"note=\"{note}\""));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("idempotent_orders").EnumerateArray().ToArray();
    }
}
