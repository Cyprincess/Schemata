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

    public EventPublishingJobLifecycleObserver(
        IEventBus                                eventBus,
        IOptions<SchemataSchedulingEventOptions> options
    ) {
        _eventBus = eventBus;
        _options  = options.Value;
    }

    #region IJobLifecycleObserver Members

    public async Task OnScheduledAsync(SchemataJob job, CancellationToken ct = default) {
        var config = ResolveConfig(job);
        if (config.Result == AdviseResult.Block) {
            return;
        }

        await _eventBus.PublishAsync(new JobScheduled {
            Job     = job.Name!,
            Variables   = job.Variables,
            ScheduledAt = DateTime.UtcNow,
        }, ct);
    }

    public async Task OnUnscheduledAsync(SchemataJob job, CancellationToken ct = default) {
        var config = ResolveConfig(job);
        if (config.Result == AdviseResult.Block) {
            return;
        }

        await _eventBus.PublishAsync(new JobUnscheduled {
            Job       = job.Name!,
            UnscheduledAt = DateTime.UtcNow,
        }, ct);
    }

    public async Task<JobTriggerOutcome> OnTriggeredAsync(
        SchemataJob       job,
        JobContext        context,
        CancellationToken ct = default
    ) {
        var config = ResolveConfig(job);

        // Block at config time means: do not publish anything and stop the fire entirely;
        // the schedule does not advance.
        if (config.Result == AdviseResult.Block) {
            return JobTriggerOutcome.Block;
        }

        await _eventBus.PublishAsync(new JobTriggered {
            Job = context.Job, Variables = job.Variables,
        }, ct);

        // InterceptExecution=true means an external system takes over the run: JobTriggered
        // is published, the body is skipped, and the schedule advances. Observers wanting a
        // frozen schedule should return Block from a custom hook.
        return config.InterceptExecution ? JobTriggerOutcome.Skip : JobTriggerOutcome.Proceed;
    }

    public async Task OnSucceededAsync(SchemataJob job, JobContext context, CancellationToken ct = default) {
        var config = ResolveConfig(job);
        if (config.Result == AdviseResult.Block) {
            return;
        }

        await _eventBus.PublishAsync(new JobCompleted {
            Job     = context.Job,
            Variables   = job.Variables,
            CompletedAt = DateTime.UtcNow,
        }, ct);
    }

    public async Task OnFailedAsync(
        SchemataJob       job,
        JobContext        context,
        Exception         exception,
        CancellationToken ct = default
    ) {
        var config = ResolveConfig(job);
        if (config.Result == AdviseResult.Block) {
            return;
        }

        await _eventBus.PublishAsync(new JobFailed {
            Job   = context.Job,
            Variables = job.Variables,
            FailedAt  = DateTime.UtcNow,
            Error     = exception.ToString(),
        }, ct);
    }

    #endregion

    private (AdviseResult Result, bool InterceptExecution) ResolveConfig(SchemataJob job) {
        if (string.IsNullOrEmpty(job.JobType)) {
            return (_options.DefaultPublishEventResult, false);
        }

        var jobType = Type.GetType(job.JobType);
        if (jobType == null) {
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
