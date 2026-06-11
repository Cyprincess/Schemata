using System.Collections.Generic;
using System.Threading;
using Moq;
using Schemata.Event.Skeleton;
using Schemata.Flow.Event.Internal;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;
using SystemTask = System.Threading.Tasks.Task;

namespace Schemata.Flow.Tests;

public class FlowEventHandlerShould
{
    [Fact]
    public async SystemTask HandleAsync_DuplicateSignalSubscriptions_BroadcastsOncePerSignalName() {
        var runtime = new Mock<IProcessRuntime>();
        var context = new TestEventDispatchContext();
        context.SetSubscriptions([
            new EventSubscription("one", "invoice_paid", target: "processes/one"),
            new EventSubscription("two", "invoice_paid", target: "processes/two"),
            new EventSubscription("three", "invoice_cancelled", target: "processes/three"),
        ]);

        var handler = new FlowEventHandler(runtime.Object, context);

        await handler.HandleAsync(new TestEvent(), CancellationToken.None);

        runtime.Verify(r => r.ThrowSignalAsync("invoice_paid", It.IsAny<IEvent>(), null, It.IsAny<CancellationToken>()), Times.Once);
        runtime.Verify(r => r.ThrowSignalAsync("invoice_cancelled", It.IsAny<IEvent>(), null, It.IsAny<CancellationToken>()), Times.Once);
        runtime.VerifyNoOtherCalls();
    }

    private sealed class TestEvent : IEvent;

    private sealed class TestEventDispatchContext : IEventDispatchContext
    {
        public IReadOnlyList<IEventSubscription>? MatchedSubscriptions { get; private set; }

        public void SetSubscriptions(IReadOnlyList<IEventSubscription>? subscriptions) {
            MatchedSubscriptions = subscriptions;
        }
    }
}
