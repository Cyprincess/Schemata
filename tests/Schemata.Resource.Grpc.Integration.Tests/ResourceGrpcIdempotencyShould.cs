using System;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf.Grpc.Configuration;
using ProtoBuf.Grpc.Client;
using Schemata.Core.Building;
using Schemata.Resource.Grpc.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Grpc.Integration.Tests;

[Collection("GrpcIntegration")]
[Trait("Category", "Integration")]
public class ResourceGrpcIdempotencyShould
{
    private readonly WebAppFactory _factory;

    public ResourceGrpcIdempotencyShould(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Create_DuplicateRequestId_CreatesOneOrder_AndReturnsTheFirstResponse() {
        var (channel, clientFactory) = _factory.CreateGrpcChannelWithClient();
        var client = channel.CreateGrpcService<IResourceService<IdempotentOrder, IdempotentOrderRequest, IdempotentOrder, IdempotentOrder>>(clientFactory);

        var requestId = Guid.NewGuid().ToString();
        var note      = $"seq-{requestId}";

        var first  = await client.CreateAsync(new() { Note = note, RequestId = requestId });
        var second = await client.CreateAsync(new() { Note = note, RequestId = requestId });

        Assert.Equal(first.CanonicalName, second.CanonicalName);
        Assert.Equal(first.Uid, second.Uid);

        var listed = await client.ListAsync(new() { Filter = $"note=\"{note}\"" });
        Assert.Single(listed.Entities!);
    }

    [Fact]
    public async Task Create_ConcurrentDuplicates_ResolveToASingleMutation() {
        var (channel, clientFactory) = _factory.CreateGrpcChannelWithClient();
        var client = channel.CreateGrpcService<IResourceService<IdempotentOrder, IdempotentOrderRequest, IdempotentOrder, IdempotentOrder>>(clientFactory);

        var requestId = Guid.NewGuid().ToString();
        var note      = $"conc-{requestId}";

        var responses = await Task.WhenAll(Enumerable.Range(0, 4)
                                                     .Select(_ => client.CreateAsync(new() { Note = note, RequestId = requestId }).AsTask()));

        Assert.Single(responses.Select(r => r.CanonicalName).Distinct());

        var listed = await client.ListAsync(new() { Filter = $"note=\"{note}\"" });
        Assert.Single(listed.Entities!);
    }

    [Fact]
    public async Task Create_SameRequestIdWithDifferentBody_FailsValidation() {
        var (channel, clientFactory) = _factory.CreateGrpcChannelWithClient();
        var client = channel.CreateGrpcService<IResourceService<IdempotentOrder, IdempotentOrderRequest, IdempotentOrder, IdempotentOrder>>(clientFactory);

        var requestId = Guid.NewGuid().ToString();

        var created = await client.CreateAsync(new() { Note = $"first-{requestId}", RequestId = requestId });
        Assert.NotNull(created.CanonicalName);

        var error = await Assert.ThrowsAsync<RpcException>(() =>
            client.CreateAsync(new() { Note = $"changed-{requestId}", RequestId = requestId }).AsTask());
        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);

        var listed = await client.ListAsync(new() { Filter = $"note=\"changed-{requestId}\"" });
        Assert.True(listed.Entities is null || listed.Entities.Count == 0);
    }

    [Fact]
    public async Task Create_ReplayAfterRetentionExpiry_ExecutesFresh() {
        using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.PostConfigure<SchemataResourceOptions>(options =>
                options.IdempotencyRetention = TimeSpan.FromMilliseconds(500))));
        using var http = factory.CreateClient(new() { BaseAddress = new("http://localhost") });
        using var channel = GrpcChannel.ForAddress(http.BaseAddress!, new() { HttpClient = http });
        var clientFactory = ClientFactory.Create(factory.Services.GetRequiredService<BinderConfiguration>());
        var client = channel.CreateGrpcService<IResourceService<IdempotentOrder, IdempotentOrderRequest, IdempotentOrder, IdempotentOrder>>(clientFactory);

        var requestId = Guid.NewGuid().ToString();
        var note      = $"exp-{requestId}";

        var first = await client.CreateAsync(new() { Note = note, RequestId = requestId });

        await Task.Delay(TimeSpan.FromSeconds(1.2));

        var replay = await client.CreateAsync(new() { Note = note, RequestId = requestId });
        Assert.NotEqual(first.CanonicalName, replay.CanonicalName);

        var listed = await client.ListAsync(new() { Filter = $"note=\"{note}\"" });
        Assert.Equal(2, listed.Entities!.Count);
    }
}
