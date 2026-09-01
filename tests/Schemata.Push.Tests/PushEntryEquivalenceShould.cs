using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Push.Foundation.Commands;
using Schemata.Push.Foundation.Handlers;
using Schemata.Push.Scheduling.Features;
using Schemata.Push.Scheduling.Handlers;
using Schemata.Push.Skeleton;
using Schemata.Push.Skeleton.Entities;
using Schemata.Scheduling.Skeleton;
using Xunit;

namespace Schemata.Push.Tests;

public class PushEntryEquivalenceShould
{
    [Fact]
    public async Task Send_Facade_Forwards_Context_Through_Dispatcher_And_Isolates_Transport_Failure() {
        var captured      = new TaskCompletionSource<PushContext>();
        var pushAdvisor   = new CapturingPushAdvisor(captured);
        var services      = new ServiceCollection();
        services.AddSingleton<IRequestPipelineAdvisor<SendPushRequest, ImmutableArray<TransportResult>>, MarkerCommandAdvisor>();
        services.AddSingleton<IRequestPipelineAdvisor<SendPushRequest, ImmutableArray<TransportResult>>>(pushAdvisor);
        services.AddSingleton<IPushTransport>(new ImmediateTransport("sent"));
        services.AddSingleton<IPushTransport>(new ThrowingTransport("failed"));
        services.AddSchemataPush();
        using var provider = services.BuildServiceProvider();

        var results = await CollectAsync(provider.GetRequiredService<IPushService>()
                                              .SendAsync(new PushContext("message", new RecipientTarget("users/one"))));

        var context = await captured.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("message", context.Message);
        Assert.Equal("users/one", Assert.IsType<RecipientTarget>(context.Target).Subject);
        Assert.True(pushAdvisor.SawMarker);

        Assert.Contains(results, result => result.Transport == "sent" && result.Status == TransportStatus.Sent);
        Assert.Contains(results, result => result.Transport == "failed"
                                        && result.Status == TransportStatus.Failed
                                        && result.Error == "transport failed");
    }

    [Fact]
    public async Task Send_Facade_Waits_For_All_Transports_Before_First_Yield() {
        var first  = new TaskCompletionSource<TransportResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<TransportResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddSingleton<IPushTransport>(new DeferredTransport("first", first));
        services.AddSingleton<IPushTransport>(new DeferredTransport("second", second));
        services.AddSchemataPush();
        using var provider = services.BuildServiceProvider();
        await using var enumerator = provider.GetRequiredService<IPushService>()
                                             .SendAsync(new PushContext("message", new BroadcastTarget()))
                                             .GetAsyncEnumerator();

        var firstMove = enumerator.MoveNextAsync().AsTask();
        first.SetResult(TransportResult.Sent("first"));

        var premature = await Task.WhenAny(firstMove, Task.Delay(100));
        Assert.NotSame(firstMove, premature);

        second.SetResult(TransportResult.Sent("second"));
        Assert.True(await firstMove);
    }

    [Fact]
    public async Task GetForOwner_Facade_Materializes_Repository_Before_First_Yield() {
        var stream = new TrackingAsyncEnumerable([
            Subscription("one"),
            Subscription("two"),
            Subscription("three"),
        ]);
        var repository = new Mock<IRepository<SchemataPushSubscription>>();
        repository.Setup(value => value.ListAsync<SchemataPushSubscription>(
                             It.IsAny<Func<IQueryable<SchemataPushSubscription>, IQueryable<SchemataPushSubscription>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns(stream);
        var services = new ServiceCollection();
        services.AddSingleton(repository.Object);
        services.AddSchemataPush();
        using var provider = services.BuildServiceProvider();
        await using var enumerator = provider.GetRequiredService<IPushSubscriptionManager>()
                                             .GetForOwnerAsync("owners/one")
                                             .GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(4, stream.MoveNextCalls);
    }

    [Fact]
    public void Six_Handlers_Are_Keyed_And_Unkeyed_And_Contracts_Round_Trip() {
        var services = new ServiceCollection();
        services.AddSingleton(new Mock<IRepository<SchemataPushSubscription>>().Object);
        services.AddSingleton(new Mock<IScheduler>().Object);
        services.AddSchemataPush();
        new SchemataPushSchedulingFeature().ConfigureServices(
            services, new(), new(), null!, null!);
        using var provider = services.BuildServiceProvider();

        AssertHandler<SendPushRequest, ImmutableArray<TransportResult>, SendPushHandler>(provider);
        AssertHandler<AddPushSubscriptionRequest, PushSubscriptionResult, AddPushSubscriptionHandler>(provider);
        AssertHandler<RemovePushSubscriptionRequest, Abstractions.Unit, RemovePushSubscriptionHandler>(provider);
        AssertHandler<GetPushSubscriptionsQuery, IReadOnlyList<SchemataPushSubscription>, GetPushSubscriptionsHandler>(provider);
        AssertHandler<ExistsPushSubscriptionQuery, bool, ExistsPushSubscriptionHandler>(provider);
        AssertHandler<SchedulePushRequest, Abstractions.Resource.Operation, SchedulePushHandler>(provider);

        var send = RoundTrip(new SendPushRequest(
            new PushContext("send-message", new BroadcastTarget())));
        Assert.Equal("send-message", Assert.IsType<JsonElement>(send.Context.Message).GetString());
        Assert.IsType<BroadcastTarget>(send.Context.Target);

        var at = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var schedule = RoundTrip(new SchedulePushRequest(
            new PushContext("scheduled-message", new TopicTarget("alerts")), at));
        Assert.Equal("scheduled-message", Assert.IsType<JsonElement>(schedule.Context.Message).GetString());
        Assert.Equal("alerts", Assert.IsType<TopicTarget>(schedule.Context.Target).Topic);
        Assert.Equal(at, schedule.At);

        var add = RoundTrip(new AddPushSubscriptionRequest(
            "owners/one", "email", "primary", new() { ["locale"] = "en" }));
        Assert.Equal("owners/one", add.Owner);
        Assert.Equal("email", add.Provider);
        Assert.Equal("primary", add.ProviderKey);
        Assert.Equal("en", add.Metadata!["locale"]);

        var remove = RoundTrip(new RemovePushSubscriptionRequest("owners/one", "email", "primary"));
        Assert.Equal(("owners/one", "email", "primary"), (remove.Owner, remove.Provider, remove.ProviderKey));

        var get = RoundTrip(new GetPushSubscriptionsQuery("owners/one", "email"));
        Assert.Equal(("owners/one", "email"), (get.Owner, get.Provider));

        var exists = RoundTrip(new ExistsPushSubscriptionQuery("owners/one", "email", "primary"));
        Assert.Equal(("owners/one", "email", "primary"), (exists.Owner, exists.Provider, exists.ProviderKey));
    }

    private static T RoundTrip<T>(T request) where T : class {
        var json = JsonSerializer.Serialize(request, SchemataJson.Default);
        Assert.False(string.IsNullOrWhiteSpace(json));
        return Assert.IsType<T>(JsonSerializer.Deserialize<T>(json, SchemataJson.Default));
    }

    private static void AssertHandler<TRequest, TResponse, THandler>(IServiceProvider services)
        where TRequest : IRequest<TResponse>
        where THandler : IRequestHandler<TRequest, TResponse> {
        Assert.IsType<THandler>(services.GetRequiredService<IRequestHandler<TRequest, TResponse>>());
        Assert.IsType<THandler>(services.GetRequiredKeyedService<IRequestHandler<TRequest, TResponse>>(
            PushConstants.Handlers.Default));
    }

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source) {
        var results = new List<T>();
        await foreach (var item in source) {
            results.Add(item);
        }

        return results;
    }

    private static SchemataPushSubscription Subscription(string key) {
        return new() { Owner = "owners/one", Provider = "email", ProviderKey = key };
    }

    private sealed record Marker;

    private sealed class MarkerCommandAdvisor : IRequestPipelineAdvisor<SendPushRequest, ImmutableArray<TransportResult>>
    {
        public int Order => 0;

        public Task<ImmutableArray<TransportResult>> AdviseAsync(
            AdviceContext                                            ctx,
            SendPushRequest                                          request,
            RequestHandlerContinuation<ImmutableArray<TransportResult>> next,
            CancellationToken                                        ct = default) {
            ctx.Set(new Marker());
            return next(ct);
        }
    }

    private sealed class CapturingPushAdvisor(TaskCompletionSource<PushContext> capture)
        : IRequestPipelineAdvisor<SendPushRequest, ImmutableArray<TransportResult>>
    {
        public int Order => 0;
        public bool SawMarker { get; private set; }

        public Task<ImmutableArray<TransportResult>> AdviseAsync(
            AdviceContext                                               ctx,
            SendPushRequest                                             request,
            RequestHandlerContinuation<ImmutableArray<TransportResult>> next,
            CancellationToken                                           ct = default
        ) {
            SawMarker = ctx.TryGet<Marker>(out _);
            capture.TrySetResult(request.Context);
            return next(ct);
        }
    }

    private sealed class ImmediateTransport(string name) : IPushTransport
    {
        public string Name => name;

        public ValueTask<TransportResult> TrySendAsync(PushContext context, CancellationToken ct) {
            return ValueTask.FromResult(TransportResult.Sent(name));
        }
    }

    private sealed class ThrowingTransport(string name) : IPushTransport
    {
        public string Name => name;

        public ValueTask<TransportResult> TrySendAsync(PushContext context, CancellationToken ct) {
            throw new InvalidOperationException("transport failed");
        }
    }

    private sealed class DeferredTransport(
        string                                name,
        TaskCompletionSource<TransportResult> completion
    ) : IPushTransport
    {
        public string Name => name;

        public ValueTask<TransportResult> TrySendAsync(PushContext context, CancellationToken ct) {
            return new(completion.Task);
        }
    }

    private sealed class TrackingAsyncEnumerable(IReadOnlyList<SchemataPushSubscription> rows)
        : IAsyncEnumerable<SchemataPushSubscription>, IAsyncEnumerator<SchemataPushSubscription>
    {
        private int _index = -1;

        public int MoveNextCalls { get; private set; }

        public SchemataPushSubscription Current => rows[_index];

        public IAsyncEnumerator<SchemataPushSubscription> GetAsyncEnumerator(CancellationToken ct = default) {
            return this;
        }

        public ValueTask<bool> MoveNextAsync() {
            MoveNextCalls++;
            return ValueTask.FromResult(++_index < rows.Count);
        }

        public ValueTask DisposeAsync() { return ValueTask.CompletedTask; }
    }
}
