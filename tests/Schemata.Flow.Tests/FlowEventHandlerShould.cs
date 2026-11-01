using System;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Schemata.Flow.Event.Features;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Event.Foundation.Runtime;
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Entities;
using Schemata.Flow.Event.Handlers;
using Schemata.Flow.Foundation;
using Schemata.Flow.Foundation.Commands;
using Schemata.Messaging.Skeleton;
using Xunit;

using Schemata.Abstractions.Advisors;
namespace Schemata.Flow.Tests;

/// <summary>
///     The event bridge delivers a broadcast signal through the matched subscription rows: one
///     delivery per target process, addressed by the row that matched.
/// </summary>
public class FlowEventHandlerShould
{
    [Fact]
    [Trait("Layer", "Unit")]
    public async Task Deliver_Signal_Once_Per_Process_When_Several_Rows_Share_The_Target() {
        var delivery = new Mock<IRequestHandler<DeliverSignalRequest, SignalDeliveryResult>>();
        delivery.Setup(h => h.HandleAsync(It.IsAny<DeliverSignalRequest>(),It.IsAny<AdviceContext>(),  It.IsAny<CancellationToken>()))
                .ReturnsAsync((DeliverSignalRequest request, AdviceContext _, CancellationToken _) =>
                     new SignalDeliveryResult(request.ProcessCanonicalName, SignalDeliveryStatus.Delivered));

        var services = new ServiceCollection();
        services.AddInProcessRequestDispatcher();
        services.AddSingleton(delivery.Object);
        await using var root = services.BuildServiceProvider();

        var dispatch = new EventDispatchContext();
        dispatch.SetSubscriptions([
            new() { SubscriptionId = "flow:processes/p1:catch-a:broadcast", EventType = "sig", Target = "processes/p1" },
            new() { SubscriptionId = "flow:processes/p1:catch-b:broadcast", EventType = "sig", Target = "processes/p1" },
            new() { SubscriptionId = "flow:processes/p2:catch-a:broadcast", EventType = "sig", Target = "processes/p2" },
        ]);

        var handler = new FlowEventHandler(root, dispatch);
        var payload = Mock.Of<IEvent>();
        await handler.HandleAsync(payload, CancellationToken.None);

        delivery.Verify(
            h => h.HandleAsync(
                It.Is<DeliverSignalRequest>(r => r.ProcessCanonicalName == "processes/p1" && r.SignalName == "sig" && ReferenceEquals(r.Payload, payload) && r.Principal == null),
                It.IsAny<AdviceContext>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        delivery.Verify(
            h => h.HandleAsync(
                It.Is<DeliverSignalRequest>(r => r.ProcessCanonicalName == "processes/p2" && r.SignalName == "sig" && ReferenceEquals(r.Payload, payload) && r.Principal == null),It.IsAny<AdviceContext>(), 
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Layer", "Integration")]
    public async Task Register_Bridge_Alongside_Consumer_CatchAll_In_Either_Order(bool consumerFirst) {
        var consumer = new Mock<IEventHandler<IEvent>>();
        consumer.Setup(value => value.HandleAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        var services = new ServiceCollection();
        services.AddSingleton<IEventDispatchContext>(new EventDispatchContext());
        if (consumerFirst) services.AddSingleton(consumer.Object);
        var feature = new SchemataFlowEventFeature();
        feature.ConfigureServices(services, new(), new(), new ConfigurationBuilder().Build(), Mock.Of<IWebHostEnvironment>());
        feature.ConfigureServices(services, new(), new(), new ConfigurationBuilder().Build(), Mock.Of<IWebHostEnvironment>());
        if (!consumerFirst) services.AddSingleton(consumer.Object);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var resolver = new HandlerResolver(scope.ServiceProvider);

        Assert.Single(resolver.ResolveHandlers<IEvent>().OfType<FlowEventHandler>());
        await resolver.InvokeEventHandlersAsync(Mock.Of<IEvent>(), EventRouting.Broadcast, CancellationToken.None);
        consumer.Verify(value => value.HandleAsync(It.IsAny<IEvent>(), CancellationToken.None), Times.Once);
    }

    [Theory]
    [InlineData(SignalDeliveryStatus.Canceled)]
    [InlineData(SignalDeliveryStatus.NoLongerWaiting)]
    [Trait("Layer", "Integration")]
    public async Task Propagate_Canceled_Delivery_But_Allow_NoLongerWaiting_Race(SignalDeliveryStatus status) {
        var delivery = new Mock<IRequestHandler<DeliverSignalRequest, SignalDeliveryResult>>();
        delivery.Setup(handler => handler.HandleAsync(It.IsAny<DeliverSignalRequest>(),It.IsAny<AdviceContext>(),  It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SignalDeliveryResult("processes/p1", status));
        var services = new ServiceCollection();
        services.AddInProcessRequestDispatcher();
        services.AddSingleton(delivery.Object);
        await using var root = services.BuildServiceProvider();
        var dispatch = new EventDispatchContext();
        dispatch.SetSubscriptions([new() { EventType = "sig", Target = "processes/p1" }]);
        var handler = new FlowEventHandler(root, dispatch);

        if (status == SignalDeliveryStatus.Canceled) {
            await Assert.ThrowsAsync<OperationCanceledException>(() => handler.HandleAsync(Mock.Of<IEvent>(), CancellationToken.None));
        } else {
            await handler.HandleAsync(Mock.Of<IEvent>(), CancellationToken.None);
        }

        delivery.Verify(value => value.HandleAsync(It.IsAny<DeliverSignalRequest>(), It.IsAny<AdviceContext>(), CancellationToken.None), Times.Once);
    }
}
