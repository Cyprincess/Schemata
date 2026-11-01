using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Abstractions;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Event.Advisors;
using Schemata.Entity.Event.Tests.Fixtures;
using Schemata.Event.Skeleton;
using Xunit;

namespace Schemata.Entity.Event.Tests;

public class AdviceCommittedPendingEventsShould
{
    [Fact]
    public async Task Publish_EventsBufferedOnACommittedEntity() {
        var bus    = new Mock<IEventBus>();
        var entity = new Widget();
        entity.Rename("hub");

        var callback = Prepare(bus, entity);
        Assert.NotNull(callback);
        await callback(CancellationToken.None);

        bus.Verify(b => b.PublishAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_EveryEventTheEntityRaised_NotJustTheFirst() {
        var bus    = new Mock<IEventBus>();
        var entity = new Widget();
        entity.Rename("first");
        entity.Rename("second");
        entity.Rename("third");

        var callback = Prepare(bus, entity);
        Assert.NotNull(callback);
        await callback(CancellationToken.None);

        bus.Verify(b => b.PublishAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public void Publish_Nothing_AtPrepareTime() {
        // Prepare is side-effect free: events leave the entity only when the post-commit
        // callback runs, so a rolled-back transaction never publishes.
        var bus    = new Mock<IEventBus>();
        var entity = new Widget();
        entity.Rename("hub");

        var callback = Prepare(bus, entity);

        Assert.NotNull(callback);
        bus.Verify(b => b.PublishAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public void Prepare_Nothing_ForAnEntityThatDoesNotBufferEvents() {
        var bus     = new Mock<IEventBus>();
        var advisor = new AdviceCommittedPendingEvents<Plain>(bus.Object);

        var callback = advisor.Prepare(new(), Operations.Create);

        Assert.Null(callback);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async Task Publish_Nothing_WhenTheEntityBufferedNoEvents() {
        var bus    = new Mock<IEventBus>();
        var entity = new Widget();

        var callback = Prepare(bus, entity);
        Assert.NotNull(callback);
        await callback(CancellationToken.None);

        bus.Verify(b => b.PublishAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async Task Drain_TheEntity_SoASecondCallbackRepublishesNothing() {
        // Two mutations capturing the same entity: only the first callback to run observes the
        // buffered events, because draining dequeues them.
        var bus    = new Mock<IEventBus>();
        var entity = new Widget();
        entity.Rename("hub");

        var first = Prepare(bus, entity);
        var second = Prepare(bus, entity);
        Assert.NotNull(first);
        Assert.NotNull(second);
        await first(CancellationToken.None);
        await second(CancellationToken.None);

        bus.Verify(b => b.PublishAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Order_SitsBeforeCacheEviction() {
        // Cache eviction is structural (repository segment runs before the resource segment);
        // within the resource segment this advisor keeps its relative order.
        var advisor = new AdviceCommittedPendingEvents<Widget>(Mock.Of<IEventBus>());

        Assert.Equal(SchemataConstants.Orders.Max - 1_000, advisor.Order);
    }

    private static Func<CancellationToken, Task>? Prepare(Mock<IEventBus> bus, Widget entity) {
        var advisor = new AdviceCommittedPendingEvents<Widget>(bus.Object);

        return advisor.Prepare(entity, Operations.Create);
    }
}
