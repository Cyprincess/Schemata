using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Schemata.Transport.Http.Linking;
using Xunit;

namespace Schemata.Push.Http.Integration.Tests;

[Trait("Category", "Integration")]
public class LinkHeaderShould
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) {
            builder.UseEnvironment("Testing");
            builder.UseContentRoot(Path.GetDirectoryName(typeof(Program).Assembly.Location)!);
        }
    }

    [Fact]
    public async Task Subscription_List_Emits_Self_And_Provider_Variants() {
        using var factory = new Factory();
        var client        = Client(factory);
        var created       = await client.PostAsync("/v1/push/subscriptions",
            Json("""{"provider":"fcm","provider_key":"link-secret"}"""));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var response = await client.GetAsync("/v1/push/subscriptions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var links = Links(response);
        Assert.Single(links, link => link.Rel == "self");
        var provider = Assert.Single(links, link => link.Rel == "alternate" && link.Title == "fcm");
        Assert.Contains("provider=fcm", provider.Target);

        var header = string.Join(", ", response.Headers.GetValues(LinkHeader.Name));
        Assert.DoesNotContain("link-secret", header);
        Assert.DoesNotContain("provider_key", header);

        var filtered = await client.GetAsync("/v1/push/subscriptions?provider=fcm");
        Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);
        var all = Assert.Single(Links(filtered), link => link.Rel == "alternate" && link.Title == "all providers");
        Assert.DoesNotContain("provider=", all.Target);
    }

    [Fact]
    public async Task Delete_Has_No_Link_Header() {
        using var factory = new Factory();
        var client        = Client(factory);
        var created       = await client.PostAsync("/v1/push/subscriptions",
            Json("""{"provider":"fcm","provider_key":"delete-secret"}"""));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var deleted = await client.SendAsync(new(HttpMethod.Delete, "/v1/push/subscriptions") {
            Content = Json("""{"provider":"fcm","provider_key":"delete-secret"}"""),
        });

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.False(deleted.Headers.Contains(LinkHeader.Name));
    }

    [Fact]
    public async Task Unauthenticated_And_Forbidden_List_Have_No_Link_Header() {
        using var factory = new Factory();

        var unauthenticated = await factory.CreateClient().GetAsync("/v1/push/subscriptions");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.False(unauthenticated.Headers.Contains(LinkHeader.Name));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Owner", "users/without-permissions");
        var forbidden = await client.GetAsync("/v1/push/subscriptions");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.False(forbidden.Headers.Contains(LinkHeader.Name));
    }

    [Fact]
    public async Task Non_List_Response_Has_No_Link_Header() {
        using var factory = new Factory();
        var client        = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Owner", "users/operator");
        client.DefaultRequestHeaders.Add("X-Permissions", "push.send");

        var response = await client.PostAsync("/v1/push/subscriptions:send",
            Json("""{"message":{"title":"links"},"target":{"kind":"broadcast"},"metadata":{}}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains(LinkHeader.Name));
    }

    private static HttpClient Client(WebApplicationFactory<Program> factory) {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Owner", "users/link");
        client.DefaultRequestHeaders.Add("X-Permissions",
            "push.subscriptions.create,push.subscriptions.get,push.subscriptions.delete");

        return client;
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static IReadOnlyList<WebLink> Links(HttpResponseMessage response) {
        Assert.True(response.Headers.TryGetValues(LinkHeader.Name, out var values), "The response carries no Link header.");
        return LinkHeader.Parse(string.Join(", ", values), response.RequestMessage!.RequestUri);
    }
}
