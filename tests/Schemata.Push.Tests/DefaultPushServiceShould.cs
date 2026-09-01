using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Push.Foundation;
using Schemata.Push.Foundation.Commands;
using Schemata.Push.Skeleton;
using Xunit;

namespace Schemata.Push.Tests;

public class DefaultPushServiceShould
{

    sealed class FakeTransport(
        string name,
        Func<PushContext, CancellationToken, ValueTask<TransportResult>> send
    ) : IPushTransport
    {
        public string Name => name;

        public ValueTask<TransportResult> TrySendAsync(PushContext context, CancellationToken ct)
            => send(context, ct);
    }

    sealed class OrderedAdvisor : IRequestPipelineAdvisor<SendPushRequest, ImmutableArray<TransportResult>>
    {
        private readonly int _order;
        private readonly IList<int> _log;

        public OrderedAdvisor(int order, IList<int> log) {
            _order = order;
            _log = log;
        }

        public int Order => _order;

        public Task<ImmutableArray<TransportResult>> AdviseAsync(
            AdviceContext                                               ctx,
            SendPushRequest                                             request,
            RequestHandlerContinuation<ImmutableArray<TransportResult>> next,
            CancellationToken                                           ct = default
        ) {
            _log.Add(_order);
            return next(ct);
        }
    }

    sealed class BlockingAdvisor : IRequestPipelineAdvisor<SendPushRequest, ImmutableArray<TransportResult>>
    {
        public int Order => 0;

        public Task<ImmutableArray<TransportResult>> AdviseAsync(
            AdviceContext                                               ctx,
            SendPushRequest                                             request,
            RequestHandlerContinuation<ImmutableArray<TransportResult>> next,
            CancellationToken                                           ct = default
        ) => Task.FromResult(ImmutableArray<TransportResult>.Empty);
    }


    [Fact]
    public async Task Run_Advisors_In_Ascending_Order() {
        var log = new List<int>();
        var services = new ServiceCollection();
        services.AddSingleton<IRequestPipelineAdvisor<SendPushRequest, ImmutableArray<TransportResult>>>(new OrderedAdvisor(order: 3, log));
        services.AddSingleton<IRequestPipelineAdvisor<SendPushRequest, ImmutableArray<TransportResult>>>(new OrderedAdvisor(order: 1, log));
        services.AddSingleton<IRequestPipelineAdvisor<SendPushRequest, ImmutableArray<TransportResult>>>(new OrderedAdvisor(order: 2, log));
        services.AddSingleton<IPushTransport>(new FakeTransport("t1", (_, _) =>
            new ValueTask<TransportResult>(TransportResult.Sent("t1"))));

        using var sp = BuildServices(services);
        var sut = sp.GetRequiredService<IPushService>();

        await foreach (var _ in sut.SendAsync(new PushContext("msg", new TopicTarget("topic")), default)) { }

        Assert.Equal([1, 2, 3], log);
    }

    [Fact]
    public async Task Block_Advisor_Prevents_All_Transports() {
        var invoked      = false;
        var advisorCalls = new List<int>();
        var services     = new ServiceCollection();
        services.AddSingleton<IRequestPipelineAdvisor<SendPushRequest, ImmutableArray<TransportResult>>>(new BlockingAdvisor());
        services.AddSingleton<IRequestPipelineAdvisor<SendPushRequest, ImmutableArray<TransportResult>>>(new OrderedAdvisor(order: 1, advisorCalls));
        services.AddSingleton<IPushTransport>(new FakeTransport("t1", (_, _) => {
            invoked = true;
            return new ValueTask<TransportResult>(TransportResult.Sent("t1"));
        }));

        using var sp = BuildServices(services);
        var sut = sp.GetRequiredService<IPushService>();

        var results = new List<TransportResult>();
        await foreach (var r in sut.SendAsync(new PushContext("msg", new TopicTarget("topic")), default)) {
            results.Add(r);
        }

        Assert.Empty(results);
        Assert.Empty(advisorCalls);
        Assert.False(invoked);
    }

    [Fact]
    public async Task Wait_For_All_Transport_Results_Before_First_Yield() {
        var slow   = new TaskCompletionSource<TransportResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var middle = new TaskCompletionSource<TransportResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fast   = new TaskCompletionSource<TransportResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddSingleton<IPushTransport>(new FakeTransport(
            "slow", (_, _) => new ValueTask<TransportResult>(slow.Task)));
        services.AddSingleton<IPushTransport>(new FakeTransport(
            "middle", (_, _) => new ValueTask<TransportResult>(middle.Task)));
        services.AddSingleton<IPushTransport>(new FakeTransport(
            "fast", (_, _) => new ValueTask<TransportResult>(fast.Task)));
        using var sp = BuildServices(services);
        await using var enumerator = sp.GetRequiredService<IPushService>()
                                       .SendAsync(new PushContext("msg", new TopicTarget("topic")))
                                       .GetAsyncEnumerator();

        var first = enumerator.MoveNextAsync().AsTask();
        fast.SetResult(TransportResult.Sent("fast"));
        var premature = await Task.WhenAny(first, Task.Delay(100));
        Assert.NotSame(first, premature);

        middle.SetResult(TransportResult.Sent("middle"));
        slow.SetResult(TransportResult.Sent("slow"));
        Assert.True(await first);

        var results = new List<TransportResult> { enumerator.Current };
        while (await enumerator.MoveNextAsync()) {
            results.Add(enumerator.Current);
        }

        Assert.Equal(3, results.Count);
        Assert.Contains(results, result => result.Transport == "fast");
        Assert.Contains(results, result => result.Transport == "middle");
        Assert.Contains(results, result => result.Transport == "slow");
    }

    [Fact]
    public async Task Isolate_Throwing_Transport_While_Siblings_Still_Yield() {
        var services = new ServiceCollection();
        services.AddSingleton<IPushTransport>(new FakeTransport("good", (_, _) =>
            new ValueTask<TransportResult>(TransportResult.Sent("good", "addr-1"))));
        services.AddSingleton<IPushTransport>(new FakeTransport("throws", (_, _) =>
            throw new InvalidOperationException("transport error")));
        services.AddSingleton<IPushTransport>(new FakeTransport("also-good", (_, _) =>
            new ValueTask<TransportResult>(TransportResult.Sent("also-good", "addr-2"))));

        using var sp = BuildServices(services);
        var sut = sp.GetRequiredService<IPushService>();

        var results = new List<TransportResult>();
        await foreach (var r in sut.SendAsync(new PushContext("msg", new TopicTarget("topic")), default)) {
            results.Add(r);
        }

        Assert.Equal(3, results.Count);

        var good = results.Single(r => r.Transport == "good");
        Assert.Equal(TransportStatus.Sent, good.Status);
        Assert.Equal("addr-1", good.Address);

        var alsoGood = results.Single(r => r.Transport == "also-good");
        Assert.Equal(TransportStatus.Sent, alsoGood.Status);
        Assert.Equal("addr-2", alsoGood.Address);

        var failed = results.Single(r => r.Transport == "throws");
        Assert.Equal(TransportStatus.Failed, failed.Status);
        Assert.Equal("transport error", failed.Error);
    }

    [Fact]
    public async Task Start_All_Transports_Before_First_Result_Is_Yielded() {
        var started = 0;
        var completions = Enumerable.Range(0, 3)
                                    .Select(_ => new TaskCompletionSource<TransportResult>(
                                                TaskCreationOptions.RunContinuationsAsynchronously))
                                    .ToArray();
        var services = new ServiceCollection();
        for (var index = 0; index < completions.Length; index++) {
            var captured = index;
            services.AddSingleton<IPushTransport>(new FakeTransport($"t{captured}", (_, _) => {
                Interlocked.Increment(ref started);
                return new ValueTask<TransportResult>(completions[captured].Task);
            }));
        }

        using var sp = BuildServices(services);
        await using var enumerator = sp.GetRequiredService<IPushService>()
                                       .SendAsync(new PushContext("msg", new TopicTarget("topic")))
                                       .GetAsyncEnumerator();
        var first = enumerator.MoveNextAsync().AsTask();

        Assert.Equal(3, Volatile.Read(ref started));

        for (var index = 0; index < completions.Length; index++) {
            completions[index].SetResult(TransportResult.Sent($"t{index}"));
        }
        Assert.True(await first);
    }

    private static ServiceProvider BuildServices(IServiceCollection services) {
        services.AddSchemataPush();
        return services.BuildServiceProvider();
    }
}
