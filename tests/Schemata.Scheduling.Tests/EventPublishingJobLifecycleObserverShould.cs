using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Event.Skeleton;
using Schemata.Scheduling.Event;
using Schemata.Scheduling.Event.Events;
using Schemata.Scheduling.Event.Internal;
using Schemata.Scheduling.Foundation.Internal;
using Schemata.Scheduling.Skeleton;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class EventPublishingJobLifecycleObserverShould
{
    [Fact]
    public async Task PublishTypedVariables_FromJobContextWhenTriggered() {
        var bus = new CapturingEventBus();
        var observer
            = new EventPublishingJobLifecycleObserver(bus, Options.Create(new SchemataSchedulingEventOptions()),
                                                      Registry());
        var variables = new Dictionary<string, object?> { ["processName"] = "processes/1" };

        await observer.OnTriggeredAsync(new() { Name = "jobs/1" }, new() { Job = "jobs/1", Variables = variables });

        var @event = Assert.IsType<JobTriggered>(bus.Event);
        Assert.Same(variables, @event.Variables);
    }

    [Fact]
    public async Task PublishTypedVariables_FromPersistedJobWhenScheduled() {
        var bus = new CapturingEventBus();
        var observer
            = new EventPublishingJobLifecycleObserver(bus, Options.Create(new SchemataSchedulingEventOptions()),
                                                      Registry());

        await observer.OnScheduledAsync(new() { Name = "jobs/1", Variables = "{\"processName\":\"processes/1\"}" });

        var @event    = Assert.IsType<JobScheduled>(bus.Event);
        var variables = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(@event.Variables);
        Assert.Equal("processes/1", variables["processName"]?.ToString());
    }

    [Fact]
    public async Task PublishExceptionMessageOnly_WhenJobFails() {
        var bus = new CapturingEventBus();
        var observer
            = new EventPublishingJobLifecycleObserver(bus, Options.Create(new SchemataSchedulingEventOptions()),
                                                      Registry());

        await observer.OnFailedAsync(new() { Name = "jobs/1" },
                                     new() { Job  = "jobs/1", Variables = new Dictionary<string, object?>() },
                                     new InvalidOperationException("Boom"));

        var @event = Assert.IsType<JobFailed>(bus.Event);
        Assert.Equal("Boom", @event.Error);
    }

    private static IScheduledJobRegistry Registry() { return new DefaultScheduledJobRegistry(); }

    #region Nested type: CapturingEventBus

    private sealed class CapturingEventBus : IEventBus
    {
        public object? Event { get; private set; }

        #region IEventBus Members

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
            where TEvent : IEvent {
            Event = @event;
            return Task.CompletedTask;
        }

        public Task<TResponse> SendAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
            where TRequest : IRequest<TResponse> {
            throw new NotSupportedException();
        }

        #endregion
    }

    #endregion
}
