using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Schemata.Abstractions.Advisors;
using Schemata.Advice;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Flow.Skeleton.Runtime;

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
        var observers = services.GetServices<IProcessLifecycleObserver>().ToList();

        foreach (var observer in observers) {
            try {
                await observer.OnFailedAsync(process, exception, ct);
            } catch (Exception ex) {
                _logger?.LogWarning(ex,
                                    "IProcessLifecycleObserver.OnFailedAsync threw for process '{Name}'.",
                                    process.CanonicalName);
            }
        }
    }
}
