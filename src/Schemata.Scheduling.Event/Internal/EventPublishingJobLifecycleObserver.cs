using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Event.Skeleton;
using Schemata.Scheduling.Event.Attributes;
using Schemata.Scheduling.Event.Events;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Event.Internal;

/// <summary>
///     Bridges scheduled job lifecycle transitions to the <see cref="IEventBus" />, publishing
///     <see cref="JobTriggered" />, <see cref="JobCompleted" />, and <see cref="JobFailed" />.
///     Per-job overrides come from <see cref="SchemataSchedulingEventOptions.Jobs" />, then
///     <see cref="PublishEventAttribute" />, then
///     <see cref="SchemataSchedulingEventOptions.DefaultPublishEventResult" />.
/// </summary>
public sealed class EventPublishingJobLifecycleObserver : IJobLifecycleObserver
{
    private readonly IEventBus                      _eventBus;
    private readonly SchemataSchedulingEventOptions _options;
    private readonly IScheduledJobRegistry          _registry;
    private readonly TimeProvider                   _time;

    public EventPublishingJobLifecycleObserver(
        IEventBus                                eventBus,
        IOptions<SchemataSchedulingEventOptions> options,
        IScheduledJobRegistry                    registry,
        TimeProvider?                            time = null
    ) {
        _eventBus = eventBus;
        _options  = options.Value;
        _registry = registry;
        _time     = time ?? TimeProvider.System;
    }

    #region IJobLifecycleObserver Members

    public Task OnScheduledAsync(SchemataJob job, CancellationToken ct = default) {
        return PublishIfAllowedAsync(job, () => new JobScheduled {
            Job         = job.CanonicalName,
            Variables   = job.Variables,
            ScheduledAt = _time.GetUtcNow().UtcDateTime,
        }, ct);
    }

    public Task OnUnscheduledAsync(SchemataJob job, CancellationToken ct = default) {
        return PublishIfAllowedAsync(job, () => new JobUnscheduled {
            Job           = job.CanonicalName,
            UnscheduledAt = _time.GetUtcNow().UtcDateTime,
        }, ct);
    }

    public Task OnTriggeredAsync(
        SchemataJob       job,
        JobContext        context,
        CancellationToken ct = default
    ) {
        return PublishIfAllowedAsync(job, () => new JobTriggered {
            Job = context.Job, Variables = context.Variables,
        }, ct);
    }

    public Task OnBlockedAsync(SchemataJob job, JobContext context, CancellationToken ct = default) {
        return PublishIfAllowedAsync(job, () => new JobBlocked {
            Job = context.Job, Variables = context.Variables, BlockedAt = _time.GetUtcNow().UtcDateTime,
        }, ct);
    }

    public Task OnSkippedAsync(SchemataJob job, JobContext context, CancellationToken ct = default) {
        return PublishIfAllowedAsync(job, () => new JobSkipped {
            Job = context.Job, Variables = context.Variables, SkippedAt = _time.GetUtcNow().UtcDateTime,
        }, ct);
    }

    public Task OnSucceededAsync(SchemataJob job, JobContext context, CancellationToken ct = default) {
        return PublishIfAllowedAsync(job, () => new JobCompleted {
            Job         = context.Job,
            Variables   = context.Variables,
            CompletedAt = _time.GetUtcNow().UtcDateTime,
        }, ct);
    }

    public Task OnFailedAsync(
        SchemataJob       job,
        JobContext        context,
        Exception         exception,
        CancellationToken ct = default
    ) {
        return PublishIfAllowedAsync(job, () => new JobFailed {
            Job       = context.Job,
            Variables = context.Variables,
            FailedAt  = _time.GetUtcNow().UtcDateTime,
            Error     = exception.Message,
        }, ct);
    }

    #endregion

    private Task PublishIfAllowedAsync<TEvent>(SchemataJob job, Func<TEvent> factory, CancellationToken ct)
        where TEvent : IEvent {
        return ResolveConfig(job).Result == AdviseResult.Block
            ? Task.CompletedTask
            : _eventBus.PublishAsync(factory(), ct);
    }

    private (AdviseResult Result, bool InterceptExecution) ResolveConfig(SchemataJob job) {
        if (string.IsNullOrEmpty(job.JobKey)) {
            return (_options.DefaultPublishEventResult, false);
        }

        var jobType = _registry.Resolve(job.JobKey);
        if (jobType is null) {
            return (_options.DefaultPublishEventResult, false);
        }

        if (_options.Jobs.TryGetValue(jobType, out var registration)) {
            return (registration.Result, registration.InterceptExecution);
        }

        var attr = jobType.GetCustomAttribute<PublishEventAttribute>();
        if (attr != null) {
            return (attr.Result, attr.InterceptExecution);
        }

        return (_options.DefaultPublishEventResult, false);
    }
}
