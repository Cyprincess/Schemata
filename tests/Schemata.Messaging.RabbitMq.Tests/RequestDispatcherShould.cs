using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Schemata.Messaging.RabbitMq.Runtime;
using Schemata.Messaging.Skeleton.Runtime;
using Schemata.Transport.RabbitMq;
using Xunit;

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

    private static RabbitMqRequestDispatcher CreateDispatcher(CorrelationTracker tracker) =>
        new(Options.Create(new RabbitMqRequestOptions()), null!, tracker, null!);

    private static void RegisterReplyType(RabbitMqRequestDispatcher dispatcher, string correlationId, Type responseType) {
        var replies = (ConcurrentDictionary<string, Type>)typeof(RabbitMqRequestDispatcher)
                                               .GetField("_replyTypes", BindingFlags.NonPublic | BindingFlags.Instance)!
                                               .GetValue(dispatcher)!;
        replies[correlationId] = responseType;
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
