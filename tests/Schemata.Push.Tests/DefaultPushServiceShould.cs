using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Push.Foundation;
using Schemata.Push.Skeleton;
using Schemata.Push.Skeleton.Advisors;
using Xunit;

namespace Schemata.Push.Tests;

public class DefaultPushServiceShould
{
    [Fact]
    public async Task FanOut_InvokesEveryRegisteredTransport() {
        var fcm      = FakeTransport.Sending("fcm");
        var signalr  = FakeTransport.Sending("signalr");
        var smtp     = FakeTransport.Sending("smtp");
        var service  = Service([fcm, signalr, smtp]);
        var context  = new PushContext("hello", new BroadcastTarget());

        var results = await CollectAsync(service.SendAsync(context));

        Assert.Equal(1, fcm.Invocations);
        Assert.Equal(1, signalr.Invocations);
        Assert.Equal(1, smtp.Invocations);
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal(TransportStatus.Sent, r.Status));
        Assert.Equal(["fcm", "signalr", "smtp"], results.Select(r => r.Transport).OrderBy(n => n));
    }

    [Fact]
    public async Task FanOut_WithNoTransports_YieldsNothing() {
        var service = Service([]);

        var results = await CollectAsync(service.SendAsync(new("x", new BroadcastTarget())));

        Assert.Empty(results);
    }

    [Theory]
    [InlineData(typeof(RecipientTarget))]
    [InlineData(typeof(ChannelTarget))]
    [InlineData(typeof(TopicTarget))]
    [InlineData(typeof(BroadcastTarget))]
    public async Task SelfFilter_MatchingTransportSends_OthersSkip(Type targetType) {
        var recipient = FakeTransport.Filtering<RecipientTarget>("recipient");
        var channel   = FakeTransport.Filtering<ChannelTarget>("channel");
        var topic     = FakeTransport.Filtering<TopicTarget>("topic");
        var broadcast = FakeTransport.Filtering<BroadcastTarget>("broadcast");
        var service   = Service([recipient, channel, topic, broadcast]);

        var target = MakeTarget(targetType);
        var byName = (await CollectAsync(service.SendAsync(new("m", target))))
            .ToDictionary(r => r.Transport, r => r.Status);

        foreach (var (name, expected) in new[] {
                     ("recipient", targetType == typeof(RecipientTarget)),
                     ("channel",   targetType == typeof(ChannelTarget)),
                     ("topic",     targetType == typeof(TopicTarget)),
                     ("broadcast", targetType == typeof(BroadcastTarget)),
                 }) {
            Assert.Equal(expected ? TransportStatus.Sent : TransportStatus.Skipped, byName[name]);
        }
    }

    [Fact]
    public async Task SelfFilter_CustomTarget_OnlyMatchingKindResponds() {
        var matching = new FakeTransport("matching", (ctx, _) => new(
            ctx.Target is CustomTarget { Kind: "webhook" }
                ? TransportResult.Sent("matching")
                : TransportResult.Skipped("matching")));
        var other = FakeTransport.Filtering<BroadcastTarget>("other");
        var service = Service([matching, other]);

        var target = new CustomTarget("webhook", new Dictionary<string, string?>());
        var byName = (await CollectAsync(service.SendAsync(new("m", target))))
            .ToDictionary(r => r.Transport, r => r.Status);

        Assert.Equal(TransportStatus.Sent, byName["matching"]);
        Assert.Equal(TransportStatus.Skipped, byName["other"]);
    }

    [Fact]
    public async Task Streaming_YieldsResultsInCompletionOrder() {
        var slow = FakeTransport.SendingAfter("slow", TimeSpan.FromMilliseconds(200));
        var fast = FakeTransport.SendingAfter("fast", TimeSpan.FromMilliseconds(20));
        var service = Service([slow, fast]);

        var order = new List<string>();
        await foreach (var result in service.SendAsync(new("m", new BroadcastTarget()))) {
            order.Add(result.Transport);
        }

        Assert.Equal(new[] { "fast", "slow" }, order);
    }

    [Fact]
    public async Task Streaming_OneTransportThrows_OthersStillSendAndFailureIsolated() {
        var ok      = FakeTransport.Sending("ok");
        var broken  = FakeTransport.Throwing("broken");
        var service = Service([ok, broken]);

        var byName = (await CollectAsync(service.SendAsync(new("m", new BroadcastTarget()))))
            .ToDictionary(r => r.Transport, r => r);

        Assert.Equal(TransportStatus.Sent, byName["ok"].Status);
        Assert.Equal(TransportStatus.Failed, byName["broken"].Status);
        Assert.NotNull(byName["broken"].Error);
    }

    [Fact]
    public async Task Send_RunsSendAdvisorsBeforeFanOut() {
        var transport = FakeTransport.Sending("fcm");
        var advisor   = new RecordingAdvisor(AdviseResult.Continue);
        var service   = Service([transport], [advisor]);

        await CollectAsync(service.SendAsync(new("m", new BroadcastTarget())));

        Assert.Equal(1, advisor.Invocations);
        Assert.Equal(1, transport.Invocations);
    }

    [Fact]
    public async Task Send_AdvisorBlocks_AbortsBeforeAnyTransport() {
        var transport = FakeTransport.Sending("fcm");
        var advisor   = new RecordingAdvisor(AdviseResult.Block);
        var service   = Service([transport], [advisor]);

        var results = await CollectAsync(service.SendAsync(new("m", new BroadcastTarget())));

        Assert.Empty(results);
        Assert.Equal(0, transport.Invocations);
    }

    private static PushTarget MakeTarget(Type targetType) {
        if (targetType == typeof(RecipientTarget)) return new RecipientTarget("users/chino");
        if (targetType == typeof(ChannelTarget)) return new ChannelTarget("general");
        if (targetType == typeof(TopicTarget)) return new TopicTarget("news");
        if (targetType == typeof(BroadcastTarget)) return new BroadcastTarget();
        throw new ArgumentOutOfRangeException(nameof(targetType));
    }

    private static IPushService Service(
        IReadOnlyList<IPushTransport>     transports,
        IReadOnlyList<IPushSendAdvisor>?  advisors = null
    ) {
        var services = new ServiceCollection();
        foreach (var transport in transports) {
            services.AddSingleton(transport);
        }

        foreach (var advisor in advisors ?? []) {
            services.AddSingleton<IPushSendAdvisor>(advisor);
        }

        var sp = services.BuildServiceProvider();
        return new DefaultPushService(sp);
    }

    private static async Task<List<TransportResult>> CollectAsync(IAsyncEnumerable<TransportResult> source) {
        var results = new List<TransportResult>();
        await foreach (var result in source) {
            results.Add(result);
        }

        return results;
    }
}
