using System;
using System.IO;
using System.Net.Http;
using System.Linq;
using Schemata.Push.Skeleton;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Schemata.Push.Skeleton.Entities;
using Schemata.Entity.Repository;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Push.Http.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Push.Http.Integration.Tests;

public sealed class PushSmokeShould
{
    private sealed class SmokeFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) {
            builder.UseEnvironment("Testing");
            builder.UseContentRoot(Path.GetDirectoryName(typeof(Program).Assembly.Location)!);
        }
    }

    [Fact]
    public async Task Exercise_Owner_Lifecycle_And_Partial_Send_Over_Real_Pipeline() {
        using var factory = new SmokeFactory();
        var alice = Client(factory, "users/alice", "push.subscriptions.create", "push.subscriptions.get", "push.subscriptions.delete");
        var created = await alice.PostAsync("/v1/push/subscriptions", Json("""{"provider":"fcm","provider_key":"smoke-device-token"}"""));
        var createdBody = await created.Content.ReadAsStringAsync();
        Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
        var listed = await alice.GetAsync("/v1/push/subscriptions");
        var listBody = await listed.Content.ReadAsStringAsync();
        listed.EnsureSuccessStatusCode();
        var sent = await Client(factory, "users/operator", "push.send").PostAsync("/v1/push/subscriptions:send",
            Json("""{"message":{"title":"smoke"},"target":{"kind":"broadcast"},"metadata":{}}"""));
        sent.EnsureSuccessStatusCode();
        var sendBody = await sent.Content.ReadAsStringAsync();
        using var scope = factory.Services.CreateScope();
        var rows = 0;
        await foreach (var _ in scope.ServiceProvider.GetRequiredService<IRepository<SchemataPushSubscription>>().ListAsync<SchemataPushSubscription>(null!)) rows++;
        Console.WriteLine($"PUSH_SMOKE create={(int)created.StatusCode}; secrets_echoed={createdBody.Contains("smoke-device-token")}; list_rows_listed={listBody.Contains("fcm")}; send_partial={sendBody.Contains("backend unavailable") && sendBody.Contains("\"transport\":\"ok\"")}; persisted_rows={rows}");
        Assert.DoesNotContain("smoke-device-token", createdBody);
        Assert.Equal(1, rows);
        using var response = JsonDocument.Parse(sendBody);
        Assert.Contains(response.RootElement.GetProperty("results").EnumerateArray(), r => r.GetProperty("transport").GetString() == "later" && r.GetProperty("status").GetString() == "failed");
        var receiver = factory.Services.GetServices<IPushTransport>().OfType<RecordingTransport>().Single(t => t.Name == "ok");
        var input = Assert.Single(receiver.Deliveries);
        Assert.Equal("smoke", Assert.IsType<JsonElement>(input.Message).GetProperty("title").GetString());
        Assert.IsType<BroadcastTarget>(input.Target);
        Assert.Equal(PushPriority.Normal, input.Options.Priority);
    }

    private static HttpClient Client(WebApplicationFactory<Program> factory, string owner, params string[] permissions) {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Owner", owner);
        if (permissions.Length > 0) client.DefaultRequestHeaders.Add("X-Permissions", string.Join(",", permissions));
        return client;
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");
}
