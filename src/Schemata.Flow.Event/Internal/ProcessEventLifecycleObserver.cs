using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Event.Skeleton;
using Schemata.Flow.Event.Events;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Event.Internal;

/// <summary>Publishes process and token lifecycle notifications to the event bus.</summary>
public sealed class ProcessEventLifecycleObserver : IProcessLifecycleObserver, ITokenLifecycleObserver
{
    private readonly IServiceProvider _services;

    /// <summary>Creates an observer that resolves the event bus from the active scope.</summary>
    public ProcessEventLifecycleObserver(IServiceProvider services) {
        _services = services;
    }

    #region ITokenLifecycleObserver Members

    public async Task OnTokenCancelledAsync(
        SchemataProcess   process,
        TokenSnapshot     token,
        CancellationToken ct = default
    ) {
        await PublishAsync(new TokenCancelledEvent {
            ProcessCanonicalName = process.CanonicalName!,
            TokenCanonicalName   = token.CanonicalName,
            StateName            = token.StateName,
        }, process, ct);
    }

    public async Task OnTokenForkedAsync(
        SchemataProcess   process,
        TokenSnapshot     token,
        TokenSnapshot?    spawner,
        CancellationToken ct = default
    ) {
        await PublishAsync(new TokenForkedEvent {
            ProcessCanonicalName = process.CanonicalName!,
            TokenCanonicalName   = token.CanonicalName,
            SpawnerCanonicalName = spawner?.CanonicalName,
            StateName            = token.StateName,
        }, process, ct);
    }

    public async Task OnTokenJoinedAsync(
        SchemataProcess              process,
        TokenSnapshot                output,
        IReadOnlyList<TokenSnapshot> inputs,
        CancellationToken            ct = default
    ) {
        await PublishAsync(new TokenJoinedEvent {
            ProcessCanonicalName = process.CanonicalName!,
            TokenCanonicalName   = output.CanonicalName,
            InputCanonicalNames  = inputs.Select(input => input.CanonicalName).ToArray(),
            StateName            = output.StateName,
        }, process, ct);
    }

    #endregion

    #region IProcessLifecycleObserver Members

    public async Task OnStartedAsync(SchemataProcess process, CancellationToken ct = default) {
        await PublishAsync(new ProcessStartedEvent {
            ProcessCanonicalName = process.CanonicalName!,
            DefinitionName       = process.DefinitionName,
        }, process, ct);
    }

    public async Task OnTransitionedAsync(
        SchemataProcess           process,
        SchemataProcessTransition transition,
        CancellationToken         ct = default
    ) {
        await PublishAsync(new TransitionMadeEvent {
            ProcessCanonicalName = process.CanonicalName!,
            FromStateName        = transition.Previous,
            ToStateName          = transition.Posterior,
        }, process, ct);
    }

    public async Task OnTerminatedAsync(SchemataProcess process, CancellationToken ct = default) {
        await PublishAsync(new ProcessCompletedEvent {
            ProcessCanonicalName = process.CanonicalName!,
            DefinitionName       = process.DefinitionName,
        }, process, ct);
    }

    public async Task OnFailedAsync(SchemataProcess process, Exception exception, CancellationToken ct = default) {
        await PublishAsync(new ProcessFailedEvent {
            ProcessCanonicalName = process.CanonicalName!,
            DefinitionName       = process.DefinitionName,
            ErrorMessage         = exception.Message,
        }, process, ct);
    }

    #endregion

    private async Task PublishAsync<TEvent>(TEvent @event, SchemataProcess process, CancellationToken ct)
        where TEvent : IEvent {
        var bus = _services.GetService<IEventBus>();
        if (bus is not null) {
            await bus.PublishAsync(@event, process, ct);
        }
    }

}
