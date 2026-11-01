using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using RabbitMQ.Client;
using Schemata.Advice;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Event.Foundation.Observers;
using Schemata.Event.Foundation.Runtime;
using Schemata.Event.RabbitMq;
using Schemata.Event.RabbitMq.Runtime;
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Advisors;
using Schemata.Event.Skeleton.Entities;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Runtime;
using Schemata.Transport.RabbitMq;
using Xunit;

namespace Schemata.Event.Foundation.Tests;

public class RabbitMqConsumerHostShould
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Layer", "Integration")]
    public async Task Settlement_Failure_Faults_Host_And_Disposes_Current_Channel(bool acknowledge) {
        var failure = new InvalidOperationException("settlement failed");
        var ready = new TaskCompletionSource<IAsyncBasicConsumer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var channel = new Mock<IChannel>();
        channel.Setup(value => value.ExchangeDeclareAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<bool>(), It.IsAny<IDictionary<string, object?>?>(), It.IsAny<bool>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        channel.Setup(value => value.QueueDeclareAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(),
            It.IsAny<bool>(), It.IsAny<IDictionary<string, object?>?>(), It.IsAny<bool>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(new QueueDeclareOk("events", 0, 0));
        channel.Setup(value => value.QueueBindAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IDictionary<string, object?>?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        channel.Setup(value => value.BasicQosAsync(It.IsAny<uint>(), It.IsAny<ushort>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        channel.Setup(value => value.BasicConsumeAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(),
            It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<IDictionary<string, object?>?>(),
            It.IsAny<IAsyncBasicConsumer>(), It.IsAny<CancellationToken>()))
            .Callback<string, bool, string, bool, bool, IDictionary<string, object?>?, IAsyncBasicConsumer, CancellationToken>(
                (_, _, _, _, _, _, consumer, _) => ready.TrySetResult(consumer)).ReturnsAsync("consumer");
        channel.Setup(value => value.BasicAckAsync(It.IsAny<ulong>(), false, It.IsAny<CancellationToken>())).Throws(failure);
        channel.Setup(value => value.BasicNackAsync(It.IsAny<ulong>(), false, false, It.IsAny<CancellationToken>())).Throws(failure);
        channel.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var connection = new Mock<IConnection>();
        connection.Setup(value => value.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(channel.Object);
        var connections = new Mock<IRabbitMqConnectionProvider>();
        connections.Setup(value => value.GetConnectionAsync(It.IsAny<CancellationToken>()))
                   .Returns(new ValueTask<IConnection>(connection.Object));
        var registry = new Mock<IEventTypeRegistry>();
        registry.Setup(value => value.Resolve("sample")).Returns(acknowledge ? typeof(SampleEvent) : null);
        var subscriptions = new Mock<IRepository<SchemataEventSubscription>>();
        subscriptions.Setup(value => value.ListAsync(
            It.IsAny<Func<IQueryable<SchemataEventSubscription>, IQueryable<SchemataEventSubscription>>>(),
            It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>())).Returns(EmptySubscriptions());
        await using var services = new ServiceCollection().AddSingleton(registry.Object).AddSingleton(subscriptions.Object)
            .AddSingleton<IMessageExecutionScopeFactory, MessageExecutionScopeFactory>().BuildServiceProvider();
        using var host = new RabbitMqConsumerHost(services, connections.Object, Options.Create(new RabbitMqEventOptions()),
            Options.Create(new System.Text.Json.JsonSerializerOptions()));
        await host.StartAsync(CancellationToken.None);
        var consumer = await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.HandleBasicDeliverAsync(
            "consumer", 1, false, "events", "sample", new BasicProperties(), "{}"u8.ToArray(), CancellationToken.None)));
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => host.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(30))));
        channel.Verify(value => value.DisposeAsync(), Times.Once);
        if (acknowledge) channel.Verify(value => value.BasicAckAsync(1, false, It.IsAny<CancellationToken>()), Times.Once);
        else channel.Verify(value => value.BasicNackAsync(1, false, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Layer", "Integration")]
    public async Task Correlated_Event_Still_Runs_Subscribers() {
        var observed = new TaskCompletionSource<EventContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = new Mock<IEventLifecycleObserver>();
        observer.Setup(value => value.OnConsumedAsync(It.IsAny<EventContext>(), It.IsAny<CancellationToken>()))
                .Callback<EventContext, CancellationToken>((context, _) => observed.TrySetResult(context))
                .Returns(Task.CompletedTask);
        var handler = new Mock<IEventHandler<SampleEvent>>();
        handler.Setup(value => value.HandleAsync(It.IsAny<SampleEvent>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
        var registry = new Mock<IEventTypeRegistry>();
        registry.Setup(value => value.Resolve("sample")).Returns(typeof(SampleEvent));
        registry.Setup(value => value.GetRouting(typeof(SampleEvent))).Returns(EventRouting.Broadcast);
        var subscriptions = new Mock<IRepository<SchemataEventSubscription>>();
        subscriptions.Setup(value => value.ListAsync(
            It.IsAny<Func<IQueryable<SchemataEventSubscription>, IQueryable<SchemataEventSubscription>>>(),
            It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>())).Returns(SingleSubscription());
        // No CorrelationTracker is registered anywhere: a correlation id is event metadata, so the
        // delivery must reach subscription matching and the handlers instead of a reply path.
        await using var services = new ServiceCollection()
            .AddSingleton(registry.Object).AddSingleton(subscriptions.Object)
            .AddSingleton<IEventDispatchContext>(new EventDispatchContext())
            .AddSingleton(handler.Object).AddSingleton(observer.Object)
            .AddSingleton<IMessageExecutionScopeFactory, MessageExecutionScopeFactory>().BuildServiceProvider();
        using var host = new RabbitMqConsumerHost(services, null!, Options.Create(new RabbitMqEventOptions()),
            Options.Create(new System.Text.Json.JsonSerializerOptions()));
        var delivery = new RabbitMQ.Client.Events.BasicDeliverEventArgs("consumer", 1, false, "events", "sample",
            new BasicProperties { CorrelationId = "corr-1" },
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new SampleEvent()));

        var handled = await InvokeHandleMessageAsync(host, delivery);

        Assert.True(handled);
        var context = await observed.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal("corr-1", context.CorrelationId);
        handler.Verify(value => value.HandleAsync(It.IsAny<SampleEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Layer", "Integration")]
    public async Task Consume_Advisor_Failure_Reaches_Every_Observer_And_Nacks() {
        var failure = new InvalidOperationException("consume advisor failed");
        var order   = new List<string>();
        var observed = new TaskCompletionSource<EventContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        var advisor = new Mock<IEventConsumeAdvisor>();
        advisor.Setup(value => value.AdviseAsync(It.IsAny<AdviceContext>(), It.IsAny<EventContext>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(failure);
        var observer = new Mock<IEventLifecycleObserver>();
        observer.Setup(value => value.OnConsumedAsync(It.IsAny<EventContext>(), It.IsAny<CancellationToken>()))
                .Callback<EventContext, CancellationToken>((context, _) => {
                    order.Add("observer");
                    observed.TrySetResult(context);
                }).Returns(Task.CompletedTask);
        var auditRow = new SchemataEvent { EventType = "sample", State = EventState.Recorded };
        SchemataEvent? persisted = null;
        var records = new Mock<IRepository<SchemataEvent>>();
        records.Setup(value => value.FirstOrDefaultAsync(
                   It.IsAny<Func<IQueryable<SchemataEvent>, IQueryable<SchemataEvent>>>(),
                   It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>())).ReturnsAsync(auditRow);
        records.Setup(value => value.UpdateAsync(It.IsAny<SchemataEvent>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
               .Callback((SchemataEvent updated, CancellationToken _, MutationOptions _) => {
                   persisted = updated;
                   order.Add("audit");
               }).ReturnsAsync(MutationResult.Applied);
        records.Setup(value => value.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var handler = new Mock<IEventHandler<SampleEvent>>();
        handler.Setup(value => value.HandleAsync(It.IsAny<SampleEvent>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
        var registry = new Mock<IEventTypeRegistry>();
        registry.Setup(value => value.Resolve("sample")).Returns(typeof(SampleEvent));
        registry.Setup(value => value.GetRouting(typeof(SampleEvent))).Returns(EventRouting.Broadcast);
        var subscriptions = new Mock<IRepository<SchemataEventSubscription>>();
        subscriptions.Setup(value => value.ListAsync(
            It.IsAny<Func<IQueryable<SchemataEventSubscription>, IQueryable<SchemataEventSubscription>>>(),
            It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>())).Returns(SingleSubscription());
        var ready = new TaskCompletionSource<IAsyncBasicConsumer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var channel = new Mock<IChannel>();
        channel.Setup(value => value.ExchangeDeclareAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<bool>(), It.IsAny<IDictionary<string, object?>?>(), It.IsAny<bool>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        channel.Setup(value => value.QueueDeclareAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(),
            It.IsAny<bool>(), It.IsAny<IDictionary<string, object?>?>(), It.IsAny<bool>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(new QueueDeclareOk("events", 0, 0));
        channel.Setup(value => value.QueueBindAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IDictionary<string, object?>?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        channel.Setup(value => value.BasicQosAsync(It.IsAny<uint>(), It.IsAny<ushort>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        channel.Setup(value => value.BasicConsumeAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(),
            It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<IDictionary<string, object?>?>(),
            It.IsAny<IAsyncBasicConsumer>(), It.IsAny<CancellationToken>()))
            .Callback<string, bool, string, bool, bool, IDictionary<string, object?>?, IAsyncBasicConsumer, CancellationToken>(
                (_, _, _, _, _, _, consumer, _) => ready.TrySetResult(consumer)).ReturnsAsync("consumer");
        channel.Setup(value => value.BasicAckAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
               .Returns(ValueTask.CompletedTask);
        channel.Setup(value => value.BasicNackAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
               .Returns(ValueTask.CompletedTask);
        channel.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var connection = new Mock<IConnection>();
        connection.Setup(value => value.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(channel.Object);
        var connections = new Mock<IRabbitMqConnectionProvider>();
        connections.Setup(value => value.GetConnectionAsync(It.IsAny<CancellationToken>()))
                   .Returns(new ValueTask<IConnection>(connection.Object));
        // The audit observer is registered first: audit-last ordering must hold regardless of DI order.
        await using var services = new ServiceCollection()
            .AddSingleton<IEventLifecycleObserver>(new SchemataEventAuditObserver(
                records.Object, Options.Create(new System.Text.Json.JsonSerializerOptions())))
            .AddSingleton(observer.Object)
            .AddSingleton<IEventConsumeAdvisor>(advisor.Object)
            .AddSingleton(registry.Object).AddSingleton(subscriptions.Object)
            .AddSingleton<IEventDispatchContext>(new EventDispatchContext())
            .AddSingleton(handler.Object)
            .AddSingleton<IMessageExecutionScopeFactory, MessageExecutionScopeFactory>().BuildServiceProvider();
        using var host = new RabbitMqConsumerHost(services, connections.Object, Options.Create(new RabbitMqEventOptions()),
            Options.Create(new System.Text.Json.JsonSerializerOptions()));
        await host.StartAsync(CancellationToken.None);
        var consumer = await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));

        await consumer.HandleBasicDeliverAsync("consumer", 1, false, "events", "sample",
            new BasicProperties { CorrelationId = "corr-advisor" },
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new SampleEvent()), CancellationToken.None);

        var context = await observed.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Same(failure, context.Exception);
        Assert.Equal(new[] { "observer", "audit" }, order);
        Assert.Same(auditRow, persisted);
        Assert.Equal(EventState.Failed, persisted!.State);
        Assert.Equal("consume advisor failed", persisted.RecentError);
        handler.Verify(value => value.HandleAsync(It.IsAny<SampleEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        channel.Verify(value => value.BasicNackAsync(1, false, false, It.IsAny<CancellationToken>()), Times.Once);
        channel.Verify(value => value.BasicAckAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static async Task<bool> InvokeHandleMessageAsync(
        RabbitMqConsumerHost                         host,
        RabbitMQ.Client.Events.BasicDeliverEventArgs delivery
    ) {
        var method = typeof(RabbitMqConsumerHost).GetMethod(
            "HandleMessageAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return await (Task<bool>)method!.Invoke(host, [Mock.Of<IChannel>(), delivery, CancellationToken.None])!;
    }

    private static async IAsyncEnumerable<SchemataEventSubscription> SingleSubscription() {
        yield return new SchemataEventSubscription { EventType = "sample" };
    }

    private static async IAsyncEnumerable<SchemataEventSubscription> EmptySubscriptions() {
        yield break;
    }

    public sealed class SampleEvent : IEvent;
}
