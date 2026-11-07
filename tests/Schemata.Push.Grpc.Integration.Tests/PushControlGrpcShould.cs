using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoBuf.Grpc.Client;
using ProtoBuf.Grpc.Configuration;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Push.Grpc;
using Schemata.Push.Skeleton;
using Schemata.Push.Skeleton.Entities;
using Xunit;
using static Schemata.Push.Grpc.Integration.Tests.Fixtures.PushGrpcAuthenticationHandler;

namespace Schemata.Push.Grpc.Integration.Tests;

public sealed class PushControlGrpcShould
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) {
            builder.UseEnvironment("Testing");
            builder.UseContentRoot(AppContext.BaseDirectory);
            builder.ConfigureServices(services => services.TryAddEnumerable(
                ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataPushSubscription>, BlockCreate>()));
        }
    }

    [Fact]
    public async Task Derive_Owner_On_The_Server_And_Never_Return_Credentials() {
        using var factory = new Factory();
        var alice = Client(factory, "users/alice", "push.subscriptions.create", "push.subscriptions.get", "push.subscriptions.delete");

        var created = await alice.CreateAsync(new CreatePushSubscriptionCommand {
            Provider = "fcm", ProviderKey = "device-secret", Metadata = { ["topic"] = "news" },
        });

        Assert.NotEqual(string.Empty, created.Uid);
        Assert.NotEqual(string.Empty, created.CanonicalName);
        Assert.Equal("fcm", created.Provider);
        Assert.NotNull(created.CreateTime);

        var listed = await alice.ListAsync(new() { Provider = "fcm" });
        var row = Assert.Single(listed.Subscriptions);
        Assert.Equal(created.Uid, row.Uid);
        Assert.Equal("fcm", row.Provider);

        var bob = Client(factory, "users/bob", "push.subscriptions.get");
        var bobList = await bob.ListAsync(new());
        Assert.Empty(bobList.Subscriptions);

        await Assert.ThrowsAsync<RpcException>(() => bob.DeleteAsync(new() { Provider = "fcm", ProviderKey = "device-secret" }).AsTask());

        _ = await alice.DeleteAsync(new() { Provider = "fcm", ProviderKey = "device-secret" });
        var afterDelete = await alice.ListAsync(new());
        Assert.Empty(afterDelete.Subscriptions);
    }

    [Fact]
    public async Task Return_An_Empty_View_When_Subscription_Create_Is_Not_Applied() {
        using var factory = new Factory();
        var client = Client(factory, "users/alice", "push.subscriptions.create");

        var created = await client.CreateAsync(new() { Provider = "blocked", ProviderKey = "device-secret" });

        Assert.Equal(string.Empty, created.Uid);
        Assert.Equal(string.Empty, created.CanonicalName);
        Assert.Equal(string.Empty, created.Provider);
        Assert.Null(created.CreateTime);
        Assert.Null(created.UpdateTime);
    }

    [Fact]
    public async Task Report_Each_Transport_Outcome_Separately_On_Send() {
        using var factory = new Factory();
        var operation = Client(factory, "users/operator", "push.send");

        var result = await operation.SendAsync(new() {
            MessageJson = "{\"title\":\"hi\"}",
            TargetKind = PushTargetKind.Broadcast,
        });

        Assert.Equal(2, result.Outcomes.Count);
        var ok = Assert.Single(result.Outcomes, o => o.Transport == "ok");
        Assert.Equal(TransportStatus.Sent, ok.Status);
        Assert.Equal("masked", ok.Address);
        var later = Assert.Single(result.Outcomes, o => o.Transport == "later");
        Assert.Equal(TransportStatus.Failed, later.Status);
        Assert.Equal("backend unavailable", later.Error);
    }

    [Fact]
    public async Task Reject_Send_Without_A_Message() {
        using var factory = new Factory();
        var operation = Client(factory, "users/operator", "push.send");

        var error = await Assert.ThrowsAsync<RpcException>(() => operation.SendAsync(new() { TargetKind = PushTargetKind.Broadcast }).AsTask());
        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
    }

    [Fact]
    public async Task Deny_Subscription_Verbs_Without_Their_Permission() {
        using var factory = new Factory();
        var reader = Client(factory, "users/reader", "push.subscriptions.get");

        var error = await Assert.ThrowsAsync<RpcException>(() => reader.CreateAsync(new() { Provider = "fcm", ProviderKey = "device-secret" }).AsTask());
        Assert.Equal(StatusCode.PermissionDenied, error.StatusCode);
    }

    private sealed class BlockCreate : IRepositoryAddAdvisor<SchemataPushSubscription>
    {
        public int Order => int.MaxValue;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext                         context,
            IRepository<SchemataPushSubscription> repository,
            SchemataPushSubscription              entity,
            MutationOptions                       options,
            CancellationToken                     ct
        ) {
            return Task.FromResult(entity.Provider == "blocked" ? AdviseResult.Block : AdviseResult.Continue);
        }
    }

    private static IPushControlService Client(WebApplicationFactory<Program> factory, string owner, params string[] permissions) {
        var http = factory.CreateClient(new() { BaseAddress = new("http://localhost") });
        http.DefaultRequestHeaders.Add("X-Owner", owner);
        if (permissions.Length > 0) http.DefaultRequestHeaders.Add("X-Permissions", string.Join(",", permissions));
        var channel = GrpcChannel.ForAddress(http.BaseAddress!, new() { HttpClient = http });
        var model   = factory.Services.GetRequiredService<PushGrpcModel>().Model;
        var binder  = BinderConfiguration.Create([ProtoBufMarshallerFactory.Create(model)]);
        return channel.CreateGrpcService<IPushControlService>(ClientFactory.Create(binder));
    }
}
