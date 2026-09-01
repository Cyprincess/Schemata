using System.Linq;
using Schemata.Domain.Skeleton.Tests.Fixtures;
using Xunit;

namespace Schemata.Domain.Skeleton.Tests;

public class AggregateBaseRaiseShould
{
    [Fact]
    public void Dequeue_ReturnsRaisedEventsInTheOrderTheyWereRaised() {
        var aggregate = new SampleAggregate();

        aggregate.Rename("first");
        aggregate.Rename("second");

        var names = aggregate.DequeuePendingEvents().Cast<WidgetRenamed>().Select(e => e.Name);

        Assert.Equal(["first", "second"], names);
    }

    [Fact]
    public void Dequeue_ClearsTheBuffer_SoASecondDrainReturnsNothing() {
        var aggregate = new SampleAggregate();
        aggregate.Rename("first");

        Assert.Single(aggregate.DequeuePendingEvents());
        Assert.Empty(aggregate.DequeuePendingEvents());
    }

    [Fact]
    public void Dequeue_AfterClearing_StillCollectsEventsRaisedLater() {
        var aggregate = new SampleAggregate();

        aggregate.Rename("first");
        aggregate.DequeuePendingEvents();
        aggregate.Rename("second");

        var drained = Assert.Single(aggregate.DequeuePendingEvents());

        Assert.Equal("second", Assert.IsType<WidgetRenamed>(drained).Name);
    }

    [Fact]
    public void Dequeue_OnAnAggregateThatRaisedNothing_ReturnsEmpty() {
        Assert.Empty(new SampleAggregate().DequeuePendingEvents());
    }

    [Fact]
    public void Raise_LeavesTheEventBuffered_RatherThanDispatchingIt() {
        var aggregate = new SampleAggregate();

        aggregate.Rename("first");

        // The mutation is visible immediately but the event is not: it stays in the aggregate until
        // something drains it, which is what keeps a rolled-back transaction from leaking events.
        Assert.Equal("first", aggregate.Name);
        Assert.Single(aggregate.DequeuePendingEvents());
    }
}
