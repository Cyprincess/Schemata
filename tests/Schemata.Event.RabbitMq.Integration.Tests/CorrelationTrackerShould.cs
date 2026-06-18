using System;
using System.Threading.Tasks;
using Schemata.Event.RabbitMq.Internal;
using Xunit;

namespace Schemata.Event.RabbitMq.Integration.Tests;

public class CorrelationTrackerShould
{
    [Fact]
    public async Task ReplyArrives_TimeoutCancelled() {
        using var tracker = new CorrelationTracker();
        var       tcs     = new TaskCompletionSource<string>();
        var       id      = tracker.Track(tcs, TimeSpan.FromMilliseconds(100));

        var completed = tracker.Complete(id, "reply");

        Assert.True(completed);
        Assert.Equal("reply", await tcs.Task);

        // Past the original timeout window the result must remain the reply, never a timeout.
        await Task.Delay(250);
        Assert.True(tcs.Task.IsCompletedSuccessfully);
        Assert.Equal("reply", await tcs.Task);
    }

    [Fact]
    public async Task NoReply_RaisesTimeout() {
        using var tracker = new CorrelationTracker();
        var       tcs     = new TaskCompletionSource<string>();

        tracker.Track(tcs, TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<TimeoutException>(() => tcs.Task);
    }

    [Fact]
    public async Task CompleteUnknownCorrelation_ReturnsFalse() {
        using var tracker = new CorrelationTracker();
        var       tcs     = new TaskCompletionSource<string>();
        var       id      = tracker.Track(tcs, TimeSpan.FromMilliseconds(100));

        Assert.True(tracker.Complete(id, "reply"));
        // A second completion finds nothing pending under the same id.
        Assert.False(tracker.Complete(id, "again"));

        Assert.Equal("reply", await tcs.Task);
    }
}
