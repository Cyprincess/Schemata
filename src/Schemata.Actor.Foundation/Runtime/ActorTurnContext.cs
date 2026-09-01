using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Actor.Skeleton;
using Schemata.Messaging.Skeleton;

namespace Schemata.Actor.Foundation.Runtime;

/// <summary>The per-turn <see cref="IActorContext" />: built fresh for every turn, disposed of - along with its scope - the moment the turn ends.</summary>
internal sealed class ActorTurnContext(
    ActorId    self,   IServiceProvider services, CancellationToken stopping,
    IActorRef? sender, ActorInstance    owner,    Guid              correlationId
) : IActorContext
{
    private object?    _recordedResponse;
    private Exception? _recordedFault;
    private bool       _replied;

    #region IActorContext Members

    public ActorId Self { get; } = self;

    public IServiceProvider Services { get; } = services;

    public CancellationToken Stopping { get; } = stopping;

    public IActorRef? Sender { get; } = sender;

    public Task<IActorRef> SpawnAsync(Props props) => owner.SpawnChildAsync(props);

    public Task ScheduleAsync(IMessage message, TimeSpan delay) {
        var reminders = Services.GetService<IActorReminders>();
        if (reminders is null) {
            throw new InvalidOperationException(
                "IActorContext.ScheduleAsync requires the Actor.Scheduling capability: no IActorReminders is registered.");
        }

        return reminders.ScheduleAsync(Self, message, delay, Guid.NewGuid().ToString("N"));
    }

    public ValueTask ReplyAsync<TResponse>(TResponse response, CancellationToken ct = default) {
        if (correlationId != Guid.Empty) {
            _recordedResponse = response;
            _recordedFault    = null;
            _replied          = true;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ReplyFaultAsync(Exception error, CancellationToken ct = default) {
        if (correlationId != Guid.Empty) {
            _recordedFault    = error;
            _recordedResponse = null;
            _replied          = true;
        }

        return ValueTask.CompletedTask;
    }

    #endregion

    /// <summary>
    ///     Commits whatever <see cref="ReplyAsync{TResponse}" />/<see cref="ReplyFaultAsync" />
    ///     recorded during this turn - called by the turn dispatcher only once
    ///     <see cref="IActor.OnReceiveAsync" /> has returned without throwing. A turn that throws
    ///     never calls this: it faults the Ask with the original exception instead, regardless of
    ///     any reply recorded earlier in the same turn.
    /// </summary>
    internal void CommitReply() {
        if (correlationId == Guid.Empty) {
            return; // The turn was triggered by a Tell: no pending reply to commit.
        }

        if (!_replied) {
            owner.FaultPendingReply(correlationId,
                                    new InvalidOperationException($"Actor '{Self}' completed the turn without replying to correlation '{correlationId}'."));
            return;
        }

        if (_recordedFault is not null) {
            owner.FaultPendingReply(correlationId, _recordedFault);
        } else {
            owner.CompletePendingReply(correlationId, _recordedResponse);
        }
    }
}