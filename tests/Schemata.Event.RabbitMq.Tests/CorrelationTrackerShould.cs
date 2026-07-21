using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Schemata.Event.RabbitMq.Internal;
using Xunit;

namespace Schemata.Event.RabbitMq.Tests;

public class CorrelationTrackerShould
{
    [Fact]
    public async Task ReplyArrives_BeforeTimeout_RemainsCompleted_WhenTimeAdvances() {
        var timeProvider = new FakeTimeProvider();
        using var tracker = new CorrelationTracker(timeProvider);
        var tcs = new TaskCompletionSource<string>();
        var id = tracker.Track(tcs, TimeSpan.FromMilliseconds(100));

        Assert.True(tracker.Complete(id, "reply"));
        timeProvider.Advance(TimeSpan.FromMilliseconds(100));

        Assert.Equal("reply", await tcs.Task);
    }

    [Fact]
    public async Task NoReply_RaisesTimeout_WhenTimeAdvances() {
        var timeProvider = new FakeTimeProvider();
        using var tracker = new CorrelationTracker(timeProvider);
        var tcs = new TaskCompletionSource<string>();

        tracker.Track(tcs, TimeSpan.FromMilliseconds(50));
        timeProvider.Advance(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<TimeoutException>(async () => await tcs.Task);
    }

    [Fact]
    public async Task CompleteUnknownCorrelation_ReturnsFalse() {
        using var tracker = new CorrelationTracker();
        var tcs = new TaskCompletionSource<string>();
        var id = tracker.Track(tcs, TimeSpan.FromMilliseconds(100));

        Assert.True(tracker.Complete(id, "reply"));
        Assert.False(tracker.Complete(id, "again"));

        Assert.Equal("reply", await tcs.Task);
    }
}
