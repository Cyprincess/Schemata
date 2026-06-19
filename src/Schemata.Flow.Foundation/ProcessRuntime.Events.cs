using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Schemata.Abstractions.Advisors;
using Schemata.Advice;
using Schemata.Event.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Events;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Skeleton.Utilities;

namespace Schemata.Flow.Foundation;

/// <summary>Coordinates process runtime operations against registered Flow engines.</summary>
public sealed partial class ProcessRuntime
{
    private async Task ProvisionFlowTransitionAsync(IServiceProvider services, FlowTransitionContext context, CancellationToken ct) {
        var advice = new AdviceContext(services);

        // Runs inside the transition's pre-commit window: each advisor provisions the wake-up
        // infrastructure the new waiting state needs. A failure aborts the transition before the
        // instance can become stranded.
        await Advisor.For<IFlowTransitionAdvisor>()
                     .RunAsync(advice, context, ct);
    }

    private async Task NotifyStartedAsync(IServiceProvider services, SchemataProcess process, CancellationToken ct) {
        var observers = services.GetServices<IProcessLifecycleObserver>().ToList();

        await PublishAsync(services, new ProcessStartedEvent {
            ProcessCanonicalName = process.CanonicalName!,
            DefinitionName       = process.DefinitionName,
            Variables            = DeserializeVariables(process),
        }, process, ct);

        foreach (var observer in observers) {
            try {
                await observer.OnStartedAsync(process, ct);
            } catch (Exception ex) {
                _logger?.LogWarning(ex,
                                    "IProcessLifecycleObserver.OnStartedAsync threw for process '{Name}'.",
                                    process.CanonicalName);
            }
        }
    }

    private async Task NotifyTransitionedAsync(
        IServiceProvider          services,
        SchemataProcess           process,
        SchemataProcessTransition transition,
        CancellationToken         ct
    ) {
        var observers = services.GetServices<IProcessLifecycleObserver>().ToList();

        await PublishAsync(services, new TransitionMadeEvent {
            ProcessCanonicalName = process.CanonicalName!,
            FromStateId          = transition.Previous,
            ToStateId            = transition.Posterior,
            WaitingAtId          = process.WaitingAtId,
        }, process, ct);

        foreach (var observer in observers) {
            try {
                await observer.OnTransitionedAsync(process, transition, ct);
            } catch (Exception ex) {
                _logger?.LogWarning(ex,
                                    "IProcessLifecycleObserver.OnTransitionedAsync threw for process '{Name}'.",
                                    process.CanonicalName);
            }
        }
    }

    private async Task NotifyTerminatedAsync(IServiceProvider services, SchemataProcess process, CancellationToken ct) {
        var observers = services.GetServices<IProcessLifecycleObserver>().ToList();

        await PublishAsync(services, new ProcessCompletedEvent {
            ProcessCanonicalName = process.CanonicalName!,
            DefinitionName       = process.DefinitionName,
            Variables            = DeserializeVariables(process),
        }, process, ct);

        foreach (var observer in observers) {
            try {
                await observer.OnTerminatedAsync(process, ct);
            } catch (Exception ex) {
                _logger?.LogWarning(ex,
                                    "IProcessLifecycleObserver.OnTerminatedAsync threw for process '{Name}'.",
                                    process.CanonicalName);
            }
        }
    }

    private async Task PublishFailedAsync(IServiceProvider services, SchemataProcess process, Exception exception, CancellationToken ct) {
        await PublishAsync(services, new ProcessFailedEvent {
            ProcessCanonicalName = process.CanonicalName!,
            DefinitionName       = process.DefinitionName,
            ErrorMessage         = exception.Message,
        }, process, ct);
    }

    private static async Task PublishAsync<TEvent>(
        IServiceProvider services,
        TEvent           @event,
        SchemataProcess  process,
        CancellationToken ct
    ) where TEvent : IEvent {
        var bus = services.GetService<IEventBus>();
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
