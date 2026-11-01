using System;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Schemata.Abstractions;
using Schemata.Messaging.RabbitMq.Runtime;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Runtime;
using Xunit;

using Schemata.Abstractions.Advisors;
namespace Schemata.Messaging.RabbitMq.Tests;

/// <summary>
///     Asserts the consumer's failure path: a handler exception is answered with an error reply
///     flagged by <see cref="RequestErrorHeaders.RemoteError" /> whose body carries only the
///     stable reason — never the exception's own details. Drives the private
///     <c>HandleAsync</c> against a mocked <see cref="IChannel" />; no broker is involved.
/// </summary>
public class RequestConsumerErrorReplyShould
{
    [Theory]
    [InlineData(typeof(OperationCanceledException), "cancelled")]
    [InlineData(typeof(InvalidOperationException), "internal")]
    public async Task HandlerFailure_PublishAnErrorEnvelope_CarryingOnlyTheStableReason(
        Type   failureType,
        string expectedReason
    ) {
        string          exchange   = null!;
        string          routingKey = null!;
        BasicProperties properties = null!;
        byte[]          body       = null!;

        var channel = new Mock<IChannel>();
        channel.Setup(c => c.BasicPublishAsync<BasicProperties>(
                       It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                       It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
              .Callback<string, string, bool, BasicProperties, ReadOnlyMemory<byte>, CancellationToken>(
                  (e, rk, _, p, b, _) => {
                      exchange   = e;
                      routingKey = rk;
                      properties = p;
                      body       = b.ToArray();
                  })
              .Returns(ValueTask.CompletedTask);

        var services = new ServiceCollection();
        services.AddScoped<InProcessRequestDispatcher>();
        services.AddScoped<IRequestHandler<Ping, Unit>>(_ => new ExplodingPingHandler(failureType));
        await using var provider = services.BuildServiceProvider();

        var host = new RabbitMqRequestConsumerHost(
            Options.Create(new RabbitMqRequestOptions().Register<Ping, Unit>("ping")),
            null!,
            new MessageExecutionScopeFactory(provider.GetRequiredService<IServiceScopeFactory>()));
        typeof(RabbitMqRequestConsumerHost)
           .GetField("_channel", BindingFlags.NonPublic | BindingFlags.Instance)!
           .SetValue(host, channel.Object);

        await (Task)typeof(RabbitMqRequestConsumerHost)
                       .GetMethod("HandleAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                       .Invoke(host, [Delivery("ping"), CancellationToken.None])!;

        Assert.Equal(string.Empty, exchange);
        Assert.Equal("reply.queue", routingKey);
        Assert.Equal("corr-42", properties.CorrelationId);
        Assert.NotNull(properties.Headers);
        Assert.True(properties.Headers!.ContainsKey(RequestErrorHeaders.RemoteError));
        // Deserialize ignores unknown fields, so pin the raw envelope: the handler's exception
        // message and any other detail must never ride along.
        var envelope = Encoding.UTF8.GetString(body);
        Assert.DoesNotContain("handler failed", envelope);
        Assert.Equal($"{{\"Reason\":\"{expectedReason}\"}}", envelope);
    }

    [Fact]
    [Trait("Layer", "Unit")]
    public async Task ErrorReply_PublishFailure_Propagates_To_The_Consumer_Lifetime() {
        var failure = new InvalidOperationException("channel failed");
        var channel = new Mock<IChannel>();
        channel.Setup(value => value.BasicPublishAsync<BasicProperties>(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<BasicProperties>(),
            It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>())).Throws(failure);
        using var services = new ServiceCollection().BuildServiceProvider();
        var host = new RabbitMqRequestConsumerHost(Options.Create(new RabbitMqRequestOptions()), null!,
            new MessageExecutionScopeFactory(services.GetRequiredService<IServiceScopeFactory>()));
        typeof(RabbitMqRequestConsumerHost).GetField("_channel", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(host, channel.Object);

        var reply = (Task)typeof(RabbitMqRequestConsumerHost)
            .GetMethod("ReplyErrorAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(host, [Delivery("ping"), new InvalidOperationException("handler failed"), CancellationToken.None])!;

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => reply));
    }

    private static BasicDeliverEventArgs Delivery(string routingKey) =>
        new("tag", 1, false, "exchange", routingKey,
            new BasicProperties { ReplyTo = "reply.queue", CorrelationId = "corr-42" },
            Encoding.UTF8.GetBytes("{}"));

    private sealed record Ping : ICommand;

    private sealed class ExplodingPingHandler(Type failureType) : IRequestHandler<Ping, Unit>
    {
        public Task<Unit> HandleAsync(Ping request, AdviceContext context, CancellationToken ct = default) =>
            throw (Exception)Activator.CreateInstance(failureType, "handler failed")!;
    }
}
