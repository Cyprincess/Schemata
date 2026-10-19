using System;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Settlement;

/// <summary>
///     The single missed-fire calculator shared by the schedule handler and the settlement owner.
/// </summary>
internal static class MissedFireWindow
{
    /// <summary>
    ///     Normalizes a requested occurrence against the configured policy. A non-replayed or
    ///     non-recurring row keeps the requested occurrence.
    /// </summary>
    internal static DateTime Adjust(
        SchemataJob      job,
        DateTime         next,
        DateTime         now,
        MissedFirePolicy policy,
        int              maxWalk
    ) {
        if (next > now || !job.Replay || job.ScheduleType is not (ScheduleType.Cron or ScheduleType.Periodic)) {
            return next;
        }

        switch (policy) {
            case MissedFirePolicy.Skip:
                for (var i = 0; i < maxWalk && next <= now; i++) {
                    var advanced = ComputeAfter(job, next);
                    if (advanced <= next) {
                        break;
                    }

                    next = advanced;
                }

                return next;

            case MissedFirePolicy.FireOnce:
                for (var i = 0; i < maxWalk; i++) {
                    var probe = ComputeAfter(job, next);
                    if (probe > now || probe <= next) {
                        break;
                    }

                    next = probe;
                }

                return next;

            case MissedFirePolicy.FireAll:
            default:
                return next;
        }
    }

    /// <summary>
    ///     Walks an occurrence past the missed window, stopping at the first future occurrence or
    ///     when the walk cap is exhausted.
    /// </summary>
    internal static DateTime AdvancePast(SchemataJob job, DateTime next, DateTime now, int maxWalk) {
        for (var i = 0; i < maxWalk && next <= now; i++) {
            var advanced = ComputeAfter(job, next);
            if (advanced <= next) {
                break;
            }

            next = advanced;
        }

        return next;
    }

    /// <summary>
    ///     Resolves the occurrence to persist: an exhausted <see cref="MissedFirePolicy.FireAll" />
    ///     replay walks past the missed window, otherwise the policy normalization applies.
    /// </summary>
    internal static DateTime Resolve(
        SchemataJob      job,
        DateTime         next,
        DateTime         now,
        MissedFirePolicy policy,
        int              maxWalk,
        int              replayedMisses
    ) {
        return policy == MissedFirePolicy.FireAll && next <= now && replayedMisses >= maxWalk - 1
            ? AdvancePast(job, next, now, maxWalk)
            : Adjust(job, next, now, policy, maxWalk);
    }

    internal static DateTime ComputeAfter(SchemataJob job, DateTime time) {
        if (job is { ScheduleType: ScheduleType.Periodic, IntervalTicks: { } ticks }) {
            return time.AddTicks(ticks);
        }

        return ScheduleDefinitionMapper.ToDefinition(job).GetNextRunTime(time) ?? DateTime.MaxValue;
    }
}
