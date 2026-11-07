using System;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Push.Grpc;
using Schemata.Transport.Http.Linking;
using Xunit;

namespace Schemata.Push.Grpc.Integration.Tests;

[Trait("Layer", "Integration")]
public class LinkHeaderShould
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) {
            builder.UseEnvironment("Testing");
            builder.UseContentRoot(AppContext.BaseDirectory);
        }
    }

    [Fact]
    public async Task List_Projects_ProviderInputs_Without_Credentials() {
        using var factory = new Factory();
        using var http = factory.CreateClient(new() { BaseAddress = new("http://localhost") });
        http.DefaultRequestHeaders.Add("X-Owner", "users/link");
        http.DefaultRequestHeaders.Add("X-Permissions", "push.subscriptions.create,push.subscriptions.get");
        using var channel = GrpcChannel.ForAddress(http.BaseAddress!, new() { HttpClient = http });
        var model = factory.Services.GetRequiredService<PushGrpcModel>();
        var invoker = channel.CreateCallInvoker();
        using var create = invoker.AsyncUnaryCall(model.Create, null, new(), new CreatePushSubscriptionCommand { Provider = "fcm", ProviderKey = "link-secret" });
        _ = await create.ResponseAsync;
        using var list = invoker.AsyncUnaryCall(model.List, null, new(), new ListPushSubscriptionsQuery());
        Assert.Single((await list.ResponseAsync).Subscriptions);
        var header = list.GetTrailers().Single(entry => entry.Key == "link").Value;
        var links = LinkHeader.Parse(header);
        Assert.Equal(new[] { "self", "alternate" }, links.Select(link => link.Rel));
        var provider = links.Single(link => link.Rel == "alternate");
        Assert.Equal(model.List.FullName, provider.Target);
        Assert.Equal("fcm", provider.Extensions!["provider"]);
        Assert.DoesNotContain("link-secret", header);
        using var filtered = invoker.AsyncUnaryCall(model.List, null, new(), new ListPushSubscriptionsQuery { Provider = "fcm" });
        Assert.Single((await filtered.ResponseAsync).Subscriptions);
        var all = Assert.Single(LinkHeader.Parse(filtered.GetTrailers().Single(entry => entry.Key == "link").Value), link => link.Rel == "alternate");
        Assert.True(all.Extensions is null || !all.Extensions.ContainsKey("provider"));
    }
}
