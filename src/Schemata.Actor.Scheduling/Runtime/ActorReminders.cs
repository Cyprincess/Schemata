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
///     into a one-time <see cref="SchemataJob" /> scheduled through <see cref="IScheduler" />, fired
///     by <see cref="ActorReminderJob" />. Reminders keep the durable schedule's default
///     <see cref="SchemataJob.Replay" /> (<see langword="true" />) so a reminder due while the
///     process was down still fires on restart, matching <see cref="IActorReminders" />'s "survives
///     process restarts" contract.
/// </summary>
public sealed class ActorReminders(
    IScheduler            scheduler,
    IScheduledJobRegistry jobRegistry,
    IServiceScopeFactory  scopeFactory,
    TimeProvider?         time = null
) : IActorReminders
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    #region IActorReminders Members

    public async Task ScheduleAsync(
        ActorId target, IMessage payload, TimeSpan delay, string reminderName, CancellationToken ct = default
    ) {
        var payloadType = payload.GetType();
        var job = new SchemataJob {
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
    }

    public async Task CancelAsync(ActorId target, string reminderName, CancellationToken ct = default) {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJob>>();
        var key = ReminderKey(target, reminderName);
        var job = await repository.FirstOrDefaultAsync<SchemataJob>(q => q.Where(job => job.Key == key), ct);
        if (job is not null) {
            await scheduler.UnscheduleAsync(job.CanonicalName!, ct);
        }
    }

    #endregion

    private static string ReminderKey(ActorId target, string reminderName) =>
        "actor-reminder:" + JsonSerializer.Serialize(new[] { target.Type, target.Key, reminderName });
}
