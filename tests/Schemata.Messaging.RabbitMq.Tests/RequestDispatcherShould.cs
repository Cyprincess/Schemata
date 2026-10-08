using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Schemata.Messaging.RabbitMq.Runtime;
using Schemata.Messaging.Skeleton.Runtime;
using Schemata.Transport.RabbitMq;
using Xunit;
using Moq;
using Schemata.Messaging.Skeleton;

namespace Schemata.Messaging.RabbitMq.Tests;

/// <summary>
///     Asserts how <see cref="RabbitMqRequestDispatcher" /> restores replies: one flagged with
///     <see cref="RequestErrorHeaders.RemoteError" /> fails the awaiter with a
///     <see cref="RemoteRequestException" /> carrying only the stable reason, while a plain reply
///     completes it with the deserialized response. Invokes the private reply handler directly —
///     the exact entry point the reply consumer calls — so no broker connection is needed.
/// </summary>
public class RequestDispatcherShould
{
    [Fact]
    public async Task Reject_Streaming_Before_Opening_A_Broker_Connection() {
        using var tracker = new CorrelationTracker();
        var connections = new Mock<IRabbitMqConnectionProvider>(MockBehavior.Strict);
        await using var dispatcher = new RabbitMqRequestDispatcher(Options.Create(new RabbitMqRequestOptions()), connections.Object, tracker, null!);
        Assert.Throws<NotSupportedException>(() => dispatcher.Stream<StreamRequest, string>(new()));
        await Assert.ThrowsAsync<NotSupportedException>(() => dispatcher.SendAsync<LazyRequest, IAsyncEnumerable<string>>(new()));
        connections.VerifyNoOtherCalls();
    }

    private sealed record StreamRequest : IStreamRequest<string>;
    private sealed record LazyRequest : IRequest<IAsyncEnumerable<string>>;

    [Fact]
    public async Task ErrorHeaderedReply_FailsTheAwaiter_WithTheStableRemoteReason() {
        using var tracker    = new CorrelationTracker();
        await using var disp = CreateDispatcher(tracker);
        var tcs = new TaskCompletionSource<string>();
        var id  = tracker.Track(tcs, TimeSpan.FromMinutes(1));
        RegisterReplyType(disp, id, typeof(string));

        await DeliverAsync(disp, Reply(id, "{\"Reason\":\"cancelled\"}", new() { [RequestErrorHeaders.RemoteError] = true }));

        var failure = await Assert.ThrowsAsync<RemoteRequestException>(async () => await tcs.Task);
        Assert.Equal("cancelled", failure.Reason);
    }

    [Fact]
    public async Task ErrorHeaderedReply_AcceptsTheFlagDeliveredAsAString() {
        using var tracker    = new CorrelationTracker();
        await using var disp = CreateDispatcher(tracker);
        var tcs = new TaskCompletionSource<string>();
        var id  = tracker.Track(tcs, TimeSpan.FromMinutes(1));
        RegisterReplyType(disp, id, typeof(string));

        await DeliverAsync(disp, Reply(id, "{\"Reason\":\"internal\"}", new() { [RequestErrorHeaders.RemoteError] = "true" }));

        var failure = await Assert.ThrowsAsync<RemoteRequestException>(async () => await tcs.Task);
        Assert.Equal("internal", failure.Reason);
    }

    [Fact]
    public async Task PlainReply_CompletesTheAwaiter_WithTheDeserializedResponse() {
        using var tracker    = new CorrelationTracker();
        await using var disp = CreateDispatcher(tracker);
        var tcs = new TaskCompletionSource<string>();
        var id  = tracker.Track(tcs, TimeSpan.FromMinutes(1));
        RegisterReplyType(disp, id, typeof(string));

        await DeliverAsync(disp, Reply(id, "\"reply\""));

        Assert.Equal("reply", await tcs.Task);
    }

    [Fact]
    public async Task PublishFailure_PropagatesTheError_AndReleasesReplyTracking() {
        using var tracker        = new CorrelationTracker();
        var       publishFailure = new InvalidOperationException("broker unreachable");
        BasicProperties? published = null;

        var publishChannel = CreatePublishChannel();
        publishChannel
            .Setup(c => c.BasicPublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                                            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(),
                                            It.IsAny<CancellationToken>()))
            .Callback<string, string, bool, BasicProperties, ReadOnlyMemory<byte>, CancellationToken>(
                (_, _, _, props, _, _) => published = props)
            .Throws(publishFailure);

        await using var dispatcher = CreateConnectedDispatcher(tracker, publishChannel.Object);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.SendAsync<PingRequest, string>(new()));
        Assert.Same(publishFailure, thrown);

        Assert.NotNull(published);
        var correlationId = published.CorrelationId!;
        Assert.False(ReplyTypes(dispatcher).ContainsKey(correlationId));
        Assert.False(tracker.Abandon(correlationId));

        await DeliverAsync(dispatcher, Reply(correlationId, "\"late\""));
        Assert.False(tracker.Abandon(correlationId));
    }

    [Fact]
    public async Task ReplyTimeout_FailsTheAwaiter_AndReleasesReplyTracking() {
        using var tracker = new CorrelationTracker();
        BasicProperties? published = null;

        var publishChannel = CreatePublishChannel();
        publishChannel
            .Setup(c => c.BasicPublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                                            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(),
                                            It.IsAny<CancellationToken>()))
            .Callback<string, string, bool, BasicProperties, ReadOnlyMemory<byte>, CancellationToken>(
                (_, _, _, props, _, _) => published = props)
            .Returns(default(ValueTask));

        await using var dispatcher = CreateConnectedDispatcher(tracker, publishChannel.Object, requestTimeoutMs: 50);

        await Assert.ThrowsAsync<TimeoutException>(() => dispatcher.SendAsync<PingRequest, string>(new()));

        var correlationId = published!.CorrelationId!;
        Assert.False(ReplyTypes(dispatcher).ContainsKey(correlationId));
        Assert.False(tracker.Abandon(correlationId));
    }

    private sealed record PingRequest : IRequest<string>;

    private static Mock<IChannel> CreatePublishChannel() {
        var channel = new Mock<IChannel>();
        channel
            .Setup(c => c.ExchangeDeclareAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(),
                                               It.IsAny<IDictionary<string, object?>?>(), It.IsAny<bool>(), It.IsAny<bool>(),
                                               It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return channel;
    }

    private static RabbitMqRequestDispatcher CreateConnectedDispatcher(
        CorrelationTracker tracker,
        IChannel           publishChannel,
        int                requestTimeoutMs = 30_000) {
        var replyChannel = new Mock<IChannel>();
        replyChannel
            .Setup(c => c.QueueDeclareAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
                                            It.IsAny<IDictionary<string, object?>?>(), It.IsAny<bool>(), It.IsAny<bool>(),
                                            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueueDeclareOk("reply", 0, 0));
        replyChannel
            .Setup(c => c.BasicConsumeAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<bool>(),
                                            It.IsAny<bool>(), It.IsAny<IDictionary<string, object?>?>(),
                                            It.IsAny<IAsyncBasicConsumer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("consumer-tag");

        var connection = new Mock<IConnection>();
        connection
            .SetupSequence(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(replyChannel.Object)
            .ReturnsAsync(publishChannel);

        var connections = new Mock<IRabbitMqConnectionProvider>();
        connections.Setup(p => p.GetConnectionAsync(It.IsAny<CancellationToken>()))
                   .Returns(new ValueTask<IConnection>(connection.Object));

        var options = Options.Create(new RabbitMqRequestOptions { RequestTimeoutMs = requestTimeoutMs }
                                        .Register<PingRequest, string>("ping"));

        return new(options, connections.Object, tracker, new ServiceCollection().BuildServiceProvider());
    }

    private static ConcurrentDictionary<string, Type> ReplyTypes(RabbitMqRequestDispatcher dispatcher) =>
        (ConcurrentDictionary<string, Type>)typeof(RabbitMqRequestDispatcher)
                                           .GetField("_replyTypes", BindingFlags.NonPublic | BindingFlags.Instance)!
                                           .GetValue(dispatcher)!;

    private static RabbitMqRequestDispatcher CreateDispatcher(CorrelationTracker tracker) =>
        new(Options.Create(new RabbitMqRequestOptions()), null!, tracker, null!);

    private static void RegisterReplyType(RabbitMqRequestDispatcher dispatcher, string correlationId, Type responseType) {
        ReplyTypes(dispatcher)[correlationId] = responseType;
    }

    private static async Task DeliverAsync(RabbitMqRequestDispatcher dispatcher, BasicDeliverEventArgs ea) {
        var handle = (Task)typeof(RabbitMqRequestDispatcher)
                          .GetMethod("HandleReplyAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                          .Invoke(dispatcher, [null, ea])!;
        await handle;
    }

    private static BasicDeliverEventArgs Reply(
        string                       correlationId,
        string                       body,
        Dictionary<string, object?>? headers = null
    ) {
        var props = new BasicProperties { CorrelationId = correlationId, ContentType = "application/json" };
        if (headers is not null) {
            props.Headers = headers;
        }

        return new("reply", 1, false, string.Empty, "reply.queue", props, Encoding.UTF8.GetBytes(body));
    }
}
