using System.Runtime.ExceptionServices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Actor.Skeleton;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Actor.Scheduling.Runtime;

/// <summary>
///     Implements <see cref="IActorReminders" /> by translating its <see cref="TimeSpan" /> delay
///     into a one-time job scheduled through <see cref="IScheduler{TJob,TExecution}" />, fired
///     by <see cref="ActorReminderJob" />. Reminders keep the durable schedule's default
///     <see cref="SchemataJob.Replay" /> (<see langword="true" />) so a reminder due while the
///     process was down still fires on restart, matching <see cref="IActorReminders" />'s "survives
///     process restarts" contract.
/// </summary>
/// <typeparam name="TJob">The persisted job entity type registered by the host.</typeparam>
/// <typeparam name="TExecution">The persisted execution entity type registered by the host.</typeparam>
public sealed class ActorReminders<TJob, TExecution>(
    IScheduler<TJob, TExecution> scheduler,
    IScheduledJobRegistry        jobRegistry,
    IMessageExecutionScopeFactory scopeFactory,
    IServiceProvider             services,
    TimeProvider?                time = null
) : IActorReminders
    where TJob : SchemataJob, new()
    where TExecution : SchemataJobExecution, new()
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    #region IActorReminders Members

    public async Task<ActorReminder> ScheduleAsync(
        ActorId target, IMessage payload, TimeSpan delay, string reminderName, CancellationToken ct = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(reminderName);
        var payloadType = payload.GetType();
        var job = new TJob {
            Key    = ReminderKey(target, reminderName),
            JobKey = jobRegistry.ResolveKey(typeof(ActorReminderJob)),
            State  = JobState.Active,
        };
        ScheduleDefinitionMapper.ApplyToJob(new OneTimeSchedule(_time.GetUtcNow().UtcDateTime + delay), job, _time);

        var variables = new Dictionary<string, string?> {
            [ActorReminderJob.ActorTypeVariable]   = target.Type,
            [ActorReminderJob.ActorKeyVariable]    = target.Key,
            [ActorReminderJob.PayloadTypeVariable] = payloadType.FullName,
            [ActorReminderJob.PayloadJsonVariable] = JsonSerializer.Serialize(payload, payloadType, SchemataJson.Default),
        };

        await scheduler.ScheduleAsync(job, variables, ct);
        return new(target, reminderName);
    }

    public async Task CancelAsync(ActorReminder reminder, CancellationToken ct = default) {
        var (target, reminderName) = reminder;
        ArgumentException.ThrowIfNullOrWhiteSpace(reminderName);
        var context = await MessageContexts.CaptureAsync(services, ct);
        var scope = await scopeFactory.CreateAsync(context, ct);
        IDisposable? lease = null;
        Exception? failure = null;
        try {
            lease = scope.Enter();
            await scope.RestoreAsync(context, ct);
            var repository = scope.Services.GetRequiredService<IRepository<TJob>>();
            var key = ReminderKey(target, reminderName);
            var job = await repository.FirstOrDefaultAsync<TJob>(q => q.Where(job => job.Key == key), ct);
            if (job is not null) await scheduler.UnscheduleAsync(job.CanonicalName!, ct);
        } catch (Exception error) { failure = error; }
        try { await scope.DisposeAsync(); }
        catch (Exception cleanup) { failure = failure is null ? cleanup : new AggregateException(failure, cleanup); }
        try { lease?.Dispose(); }
        catch (Exception cleanup) { failure = failure is null ? cleanup : new AggregateException(failure, cleanup); }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    #endregion

    private static string ReminderKey(ActorId target, string reminderName) =>
        "actor-reminder:" + JsonSerializer.Serialize(new ActorReminderKey(target.Type, target.Key, reminderName));
}
