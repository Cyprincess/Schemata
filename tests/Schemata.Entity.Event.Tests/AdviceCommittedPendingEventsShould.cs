using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Event.Advisors;
using Schemata.Entity.Event.Tests.Fixtures;
using Schemata.Entity.Repository;
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

        await Advise(bus, added: [entity]);

        bus.Verify(b => b.PublishAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_ForUpdatedAndRemovedEntitiesToo() {
        var bus     = new Mock<IEventBus>();
        var updated = new Widget();
        var removed = new Widget();
        updated.Rename("a");
        removed.Rename("b");

        // A removed aggregate can have raised events before it was deleted; dropping those would
        // lose exactly the facts a consumer needs most.
        await Advise(bus, updated: [updated], removed: [removed]);

        bus.Verify(b => b.PublishAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Publish_EveryEventTheEntityRaised_NotJustTheFirst() {
        var bus    = new Mock<IEventBus>();
        var entity = new Widget();
        entity.Rename("first");
        entity.Rename("second");
        entity.Rename("third");

        await Advise(bus, added: [entity]);

        bus.Verify(b => b.PublishAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Publish_Nothing_WhenTheEntityBufferedNoEvents() {
        var bus = new Mock<IEventBus>();

        await Advise(bus, added: [new Widget()]);

        bus.Verify(b => b.PublishAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Publish_Nothing_ForAnEntityThatDoesNotBufferEvents() {
        var bus     = new Mock<IEventBus>();
        var advisor = new AdviceCommittedPendingEvents<Plain>(bus.Object);
        var changes = new CommitChanges<Plain> { Added = [new Plain()], Updated = [], Removed = [] };

        await advisor.AdviseAsync(new(Mock.Of<IServiceProvider>()), Mock.Of<IRepository<Plain>>(), changes);

        bus.Verify(b => b.PublishAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Drain_TheEntity_SoASecondCommitRepublishesNothing() {
        var bus    = new Mock<IEventBus>();
        var entity = new Widget();
        entity.Rename("hub");

        await Advise(bus, added: [entity]);
        await Advise(bus, added: [entity]);

        bus.Verify(b => b.PublishAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Continue_TheAdvisorChain() {
        var bus = new Mock<IEventBus>();

        var result = await Advise(bus, added: [new Widget()]);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public void Order_SitsBeforeCacheEviction() {
        // Eviction runs at Orders.Max; publishing must happen while the committed state is still
        // the freshest thing any consumer could read back.
        var advisor = new AdviceCommittedPendingEvents<Widget>(Mock.Of<IEventBus>());

        Assert.Equal(SchemataConstants.Orders.Max - 1_000, advisor.Order);
    }

    private static Task<AdviseResult> Advise(
        Mock<IEventBus> bus,
        Widget[]?       added   = null,
        Widget[]?       updated = null,
        Widget[]?       removed = null
    ) {
        var advisor = new AdviceCommittedPendingEvents<Widget>(bus.Object);
        var changes = new CommitChanges<Widget> {
            Added = added ?? [], Updated = updated ?? [], Removed = removed ?? [],
        };

        return advisor.AdviseAsync(new(Mock.Of<IServiceProvider>()), Mock.Of<IRepository<Widget>>(), changes);
    }
}
