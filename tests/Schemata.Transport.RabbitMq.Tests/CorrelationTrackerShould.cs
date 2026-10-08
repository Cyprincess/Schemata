using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Schemata.Transport.RabbitMq.Tests;

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

    [Fact]
    public async Task Abandon_RemovesPending_CancelsTimeout_NoLeak() {
        var timeProvider = new FakeTimeProvider();
        using var tracker = new CorrelationTracker(timeProvider);
        var tcs = new TaskCompletionSource<string>();
        var id = tracker.Track(tcs, TimeSpan.FromMilliseconds(100));

        Assert.True(tracker.Abandon(id));
        timeProvider.Advance(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<TaskCanceledException>(async () => await tcs.Task);
        Assert.False(tracker.Abandon(id));
        Assert.False(tracker.Complete(id, "late"));
    }

    [Fact]
    public async Task Fail_SetsException_OnAwaiter() {
        using var tracker = new CorrelationTracker();
        var tcs = new TaskCompletionSource<string>();
        var id = tracker.Track(tcs, TimeSpan.FromMilliseconds(100));
        var failure = new InvalidOperationException("remote failed");

        Assert.True(tracker.Fail(id, failure));

        var caught = await Assert.ThrowsAsync<InvalidOperationException>(async () => await tcs.Task);
        Assert.Same(failure, caught);
        Assert.False(tracker.Fail(id, failure));
        Assert.False(tracker.Complete(id, "late"));
    }
}
