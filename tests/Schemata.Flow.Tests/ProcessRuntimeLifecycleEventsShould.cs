using System;
using System.Linq;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Events;
using Xunit;

namespace Schemata.Flow.Tests;

public class ProcessRuntimeLifecycleEventsShould
{
    [Fact]
    public async Task StartProcess_PublishesProcessStartedEvent_WithSchemataProcessSource() {
        var fixture = new ProcessRuntimeFixture();

        var process = await fixture.Runtime.StartProcessInstanceAsync("approval");

        var published = fixture.EventBus.Published.First(p => p.Event is ProcessStartedEvent);
        var evt = Assert.IsType<ProcessStartedEvent>(published.Event);
        Assert.Same(process, published.Source);
        Assert.Equal(process.CanonicalName, evt.ProcessCanonicalName);
        Assert.Equal("approval", evt.DefinitionName);
    }

    [Fact]
    public async Task CompleteProcess_PublishesProcessCompletedEvent() {
        var fixture = new ProcessRuntimeFixture {
            AdvanceResult = new() { StateId = "done", State = "Done", IsComplete = true },
        };
        var process = await fixture.Runtime.StartProcessInstanceAsync("approval");
        fixture.EventBus.Published.Clear();

        await fixture.Runtime.CompleteActivityAsync(process.CanonicalName!);

        var published = fixture.EventBus.Published.First(p => p.Event is ProcessCompletedEvent);
        var evt = Assert.IsType<ProcessCompletedEvent>(published.Event);
        Assert.Same(process, published.Source);
        Assert.Equal(process.CanonicalName, evt.ProcessCanonicalName);
    }

    [Fact]
    public async Task FailProcess_PublishesProcessFailedEvent_WithErrorMessage() {
        var fixture = new ProcessRuntimeFixture {
            StartException = new InvalidOperationException("start failed"),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Runtime.StartProcessInstanceAsync("approval").AsTask());

        var published = fixture.EventBus.Published.First(p => p.Event is ProcessFailedEvent);
        var evt = Assert.IsType<ProcessFailedEvent>(published.Event);
        Assert.NotNull(published.Source);
        Assert.Equal("start failed", evt.ErrorMessage);
    }

    [Fact]
    public async Task Transition_PublishesTransitionMadeEvent_WithStateIds() {
        var fixture = new ProcessRuntimeFixture();

        var process = await fixture.Runtime.StartProcessInstanceAsync("approval");

        var published = fixture.EventBus.Published.First(p => p.Event is TransitionMadeEvent);
        var evt = Assert.IsType<TransitionMadeEvent>(published.Event);
        Assert.Same(process, published.Source);
        Assert.Null(evt.FromStateId);
        Assert.Equal("Draft", evt.ToStateId);
        Assert.Null(evt.WaitingAtId);
    }
}
