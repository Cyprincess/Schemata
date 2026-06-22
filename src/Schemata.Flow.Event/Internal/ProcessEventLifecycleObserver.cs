using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Event.Skeleton;
using Schemata.Flow.Event.Events;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Skeleton.Utilities;

namespace Schemata.Flow.Event.Internal;

/// <summary>Publishes process lifecycle notifications to the event bus.</summary>
public sealed class ProcessEventLifecycleObserver : IProcessLifecycleObserver
{
    private readonly IServiceProvider _services;

    /// <summary>Creates an observer that resolves the event bus from the active scope.</summary>
    public ProcessEventLifecycleObserver(IServiceProvider services) {
        _services = services;
    }

    #region IProcessLifecycleObserver Members

    public async Task OnStartedAsync(SchemataProcess process, CancellationToken ct = default) {
        await PublishAsync(new ProcessStartedEvent {
            ProcessCanonicalName = process.CanonicalName!,
            DefinitionName       = process.DefinitionName,
            Variables            = DeserializeVariables(process),
        }, process, ct);
    }

    public async Task OnTransitionedAsync(
        SchemataProcess           process,
        SchemataProcessTransition transition,
        CancellationToken         ct = default
    ) {
        await PublishAsync(new TransitionMadeEvent {
            ProcessCanonicalName = process.CanonicalName!,
            FromStateId          = transition.Previous,
            ToStateId            = transition.Posterior,
            WaitingAtId          = process.WaitingAtId,
        }, process, ct);
    }

    public async Task OnTerminatedAsync(SchemataProcess process, CancellationToken ct = default) {
        await PublishAsync(new ProcessCompletedEvent {
            ProcessCanonicalName = process.CanonicalName!,
            DefinitionName       = process.DefinitionName,
            Variables            = DeserializeVariables(process),
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

    private static Dictionary<string, object?>? DeserializeVariables(SchemataProcess process) {
        return string.IsNullOrEmpty(process.Variables)
            ? null
            : VariableSerializer.Deserialize(process.Variables!);
    }
}
