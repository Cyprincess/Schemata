using System;
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
        delivery.Setup(h => h.HandleAsync(It.IsAny<DeliverSignalRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((DeliverSignalRequest request, CancellationToken _) =>
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
        await handler.HandleAsync(Mock.Of<IEvent>(), CancellationToken.None);

        delivery.Verify(
            h => h.HandleAsync(
                It.Is<DeliverSignalRequest>(r => r.ProcessCanonicalName == "processes/p1" && r.SignalName == "sig"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        delivery.Verify(
            h => h.HandleAsync(
                It.Is<DeliverSignalRequest>(r => r.ProcessCanonicalName == "processes/p2" && r.SignalName == "sig"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
