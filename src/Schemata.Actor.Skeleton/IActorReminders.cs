using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Messaging.Skeleton;

namespace Schemata.Actor.Skeleton;

/// <summary>
///     Durable, delayed delivery of a message to an actor, surviving process restarts. Declared
///     here so a consumer can depend on the capability without depending on whichever bridge
///     package implements it.
/// </summary>
/// <remarks>
///     <see cref="IActorContext.ScheduleAsync" /> is implemented in terms of this interface: it
///     resolves <see cref="IActorReminders" /> from the current turn's scope and forwards to
///     <see cref="ScheduleAsync" />, or throws a clear exception when no implementation is
///     registered — it never becomes a silent no-op.
/// </remarks>
public interface IActorReminders
{
    /// <summary>Schedules <paramref name="payload" /> for durable delivery to <paramref name="target" /> after <paramref name="delay" />.</summary>
    /// <param name="target">The actor to deliver the reminder to.</param>
    /// <param name="payload">The message to deliver when the reminder fires.</param>
    /// <param name="delay">The delay before delivery.</param>
    /// <param name="reminderName">
    ///     The name identifying this reminder, unique within <paramref name="target" />, used to
    ///     cancel it later with <see cref="CancelAsync" />.
    /// </param>
    /// <param name="ct">A cancellation token.</param>
    Task ScheduleAsync(ActorId target, IMessage payload, TimeSpan delay, string reminderName, CancellationToken ct = default);

    /// <summary>Cancels a previously scheduled reminder before it fires.</summary>
    /// <param name="target">The actor the reminder was scheduled against.</param>
    /// <param name="reminderName">The name passed to the corresponding <see cref="ScheduleAsync" /> call.</param>
    /// <param name="ct">A cancellation token.</param>
    Task CancelAsync(ActorId target, string reminderName, CancellationToken ct = default);
}
