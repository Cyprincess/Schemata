using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Push.Skeleton.Entities;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Push.Http.Integration.Tests;

public sealed class PushControlShould
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) {
            builder.UseEnvironment("Testing");
            builder.UseContentRoot(Path.GetDirectoryName(typeof(Program).Assembly.Location)!);
        }
    }

    [Fact]
    public async Task Derive_Owner_On_The_Server_And_Never_Return_Credentials() {
        using var factory = new Factory();
        var alice = Client(factory, "users/alice", "push.subscriptions.create", "push.subscriptions.get", "push.subscriptions.delete");
        var create = await alice.PostAsync("/v1/push/subscriptions", Json("""{"provider":"fcm","provider_key":"device-secret","metadata":{"topic":"news"}}"""));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadAsStringAsync();
        Assert.Contains("fcm", created);
        Assert.DoesNotContain("device-secret", created);
        Assert.DoesNotContain("topic", created);

        var listed = await alice.GetAsync("/v1/push/subscriptions?provider=fcm");
        listed.EnsureSuccessStatusCode();
        var listBody = await listed.Content.ReadAsStringAsync();
        Assert.Contains("fcm", listBody);
        Assert.DoesNotContain("device-secret", listBody);

        var bob = Client(factory, "users/bob", "push.subscriptions.get");
        var bobList = await bob.GetAsync("/v1/push/subscriptions");
        bobList.EnsureSuccessStatusCode();
        Assert.DoesNotContain("fcm", await bobList.Content.ReadAsStringAsync());

        var bobDelete = await bob.SendAsync(new(HttpMethod.Delete, "/v1/push/subscriptions") {
            Content = Json("""{"provider":"fcm","provider_key":"device-secret"}"""),
        });
        Assert.Equal(HttpStatusCode.Forbidden, bobDelete.StatusCode);
        var aliceList = await alice.GetAsync("/v1/push/subscriptions");
        Assert.Contains("fcm", await aliceList.Content.ReadAsStringAsync());

        var deleted = await alice.SendAsync(new(HttpMethod.Delete, "/v1/push/subscriptions") {
            Content = Json("""{"provider":"fcm","provider_key":"device-secret"}"""),
        });
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var afterDelete = await alice.GetAsync("/v1/push/subscriptions");
        Assert.DoesNotContain("fcm", await afterDelete.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Report_Each_Transport_Outcome_Separately_On_Send() {
        using var factory = new Factory();
        var operatorClient = Client(factory, "users/operator", "push.send");
        var response = await operatorClient.PostAsync("/v1/push/subscriptions:send",
            Json("""{"message":{"title":"hi"},"target":{"kind":"broadcast"},"metadata":{}}"""));
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var results = body.RootElement.GetProperty("results");
        Assert.Equal(2, results.GetArrayLength());
        Assert.Contains(results.EnumerateArray(), result => result.GetProperty("transport").GetString() == "ok" && result.GetProperty("status").GetString() == "sent");
        Assert.Contains(results.EnumerateArray(), result => result.GetProperty("transport").GetString() == "later" && result.GetProperty("error").GetString() == "backend unavailable");
    }

    private static HttpClient Client(WebApplicationFactory<Program> factory, string owner, params string[] permissions) {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Owner", owner);
        if (permissions.Length > 0) client.DefaultRequestHeaders.Add("X-Permissions", string.Join(",", permissions));
        return client;
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");
}
