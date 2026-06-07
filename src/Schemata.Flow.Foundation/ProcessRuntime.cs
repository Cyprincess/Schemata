using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Skeleton.Utilities;

namespace Schemata.Flow.Foundation;

/// <summary>
///     In-memory <see cref="IProcessRuntime" />.  Persistence is delegated to
///     <see cref="IProcessLifecycleObserver" /> implementations.
/// </summary>
public sealed class ProcessRuntime : IProcessRuntime
{
    private readonly ConcurrentDictionary<string, SchemataProcess> _instances = new();
    private readonly ILogger<ProcessRuntime>?                      _logger;
    private readonly IProcessRegistry                              _registry;
    private readonly IServiceProvider                              _services;

    public ProcessRuntime(
        IProcessRegistry         registry,
        IServiceProvider         services,
        ILogger<ProcessRuntime>? logger = null
    ) {
        _registry = registry;
        _services = services;
        _logger   = logger;
    }

    #region IProcessRuntime Members

    public ValueTask<SchemataProcess> StartProcessInstanceAsync(
        string                                processName,
        IReadOnlyDictionary<string, object?>? variables = null,
        ClaimsPrincipal?                      principal = null,
        CancellationToken                     ct        = default
    ) {
        return StartProcessInstanceCoreAsync(processName, variables, null, null, principal, ct);
    }

    public async ValueTask<ProcessInstance> CompleteActivityAsync(
        string                                instanceName,
        IReadOnlyDictionary<string, object?>? variables = null,
        ClaimsPrincipal?                      principal = null,
        CancellationToken                     ct        = default
    ) {
        var (process, definition, runtime) = Load(instanceName);

        if (variables?.Count > 0) {
            var merged = string.IsNullOrEmpty(process.Variables)
                ? new()
                : VariableSerializer.Deserialize(process.Variables!);

            foreach (var kv in variables) {
                merged[kv.Key] = kv.Value;
            }

            process.Variables = VariableSerializer.Serialize(merged);
        }

        var (instance, transition) = await ApplyAsync(process, definition, "CompleteActivity", null, principal,
                                                      c => runtime.AdvanceAsync(definition, process, c), ct);

        await NotifyTransitionedAsync(process, transition, ct);

        if (instance.IsComplete) {
            _instances.TryRemove(process.CanonicalName!, out var _);
            await NotifyTerminatedAsync(process, ct);
        }

        return instance;
    }

    public async ValueTask<ProcessInstance> CorrelateMessageAsync(
        string            instanceName,
        string            messageName,
        object?           payload   = null,
        ClaimsPrincipal?  principal = null,
        CancellationToken ct        = default
    ) {
        var (process, definition, runtime) = Load(instanceName);

        var msg = definition.Messages.FirstOrDefault(m => m.Name == messageName)
               ?? throw new NotFoundException(message: $"Message '{messageName}' not found in process definition.");

        var (instance, transition) = await ApplyAsync(process, definition, messageName, msg, principal,
                                                      c => runtime.TriggerAsync(definition, process, msg, payload, c), ct);

        await NotifyTransitionedAsync(process, transition, ct);

        if (instance.IsComplete) {
            _instances.TryRemove(process.CanonicalName!, out var _);
            await NotifyTerminatedAsync(process, ct);
        }

        return instance;
    }

    public async ValueTask ThrowSignalAsync(
        string            signalName,
        object?           payload   = null,
        ClaimsPrincipal?  principal = null,
        CancellationToken ct        = default
    ) {
        // Pure in-memory scan: only processes already in the cache and still
        // awaiting input can consume the signal.  The per-row signal-shape
        // check runs against the ProcessDefinition.
        foreach (var process in _instances.Values) {
            if (process.WaitingAtId is null) continue;

            var reg = _registry.GetRegistration(process.DefinitionName);
            if (reg is null) continue;

            var runtime = _services.GetKeyedService<IFlowRuntime>(reg.Engine);
            if (runtime is null) continue;

            var signal = reg.Definition.Signals.FirstOrDefault(s => s.Name == signalName);
            if (signal is null) continue;

            if (!MatchesSignal(reg.Definition, process, signalName)) continue;

            var (instance, transition) = await ApplyAsync(process, reg.Definition, signalName, signal, principal,
                                                          c => runtime.TriggerAsync(reg.Definition, process, signal, payload, c), ct);

            await NotifyTransitionedAsync(process, transition, ct);

            if (instance.IsComplete) {
                _instances.TryRemove(process.CanonicalName!, out var _);
                await NotifyTerminatedAsync(process, ct);
            }
        }
    }

    public async ValueTask<ProcessInstance> TriggerEventAsync(
        string            instanceName,
        IEventDefinition  trigger,
        object?           payload   = null,
        ClaimsPrincipal?  principal = null,
        CancellationToken ct        = default
    ) {
        var (process, definition, runtime) = Load(instanceName);

        var (instance, transition) = await ApplyAsync(process, definition, trigger.Name, trigger, principal,
                                                      c => runtime.TriggerAsync(definition, process, trigger, payload, c), ct);

        await NotifyTransitionedAsync(process, transition, ct);

        if (instance.IsComplete) {
            _instances.TryRemove(process.CanonicalName!, out var _);
            await NotifyTerminatedAsync(process, ct);
        }

        return instance;
    }

    public async ValueTask<ProcessInstance> TerminateProcessInstanceAsync(
        string            instanceName,
        ClaimsPrincipal?  principal = null,
        CancellationToken ct        = default
    ) {
        if (!_instances.TryGetValue(instanceName, out var process)) {
            throw new NotFoundException(message: $"Process instance '{instanceName}' not found.");
        }

        var definition = _registry.GetRegistration(process.DefinitionName)?.Definition;

        var (instance, transition) = await ApplyAsync(process, definition, "Terminate", null, principal,
                                                      _ => ValueTask.FromResult(new ProcessInstance {
                                                          StateId    = "terminated",
                                                          State      = "Terminated",
                                                          IsComplete = true,
                                                          Variables = string.IsNullOrEmpty(process.Variables)
                                                              ? new()
                                                              : VariableSerializer.Deserialize(process.Variables!),
                                                      }), ct);

        _instances.TryRemove(instanceName, out var _);

        await NotifyTransitionedAsync(process, transition, ct);
        await NotifyTerminatedAsync(process, ct);

        return instance;
    }

    #endregion

    /// <summary>Startup overload for transport layers carrying display name and description.</summary>
    public ValueTask<SchemataProcess> StartProcessInstanceAsync(
        string                       definitionName,
        string?                      displayName,
        string?                      description,
        Dictionary<string, object?>? variables,
        ClaimsPrincipal?             principal,
        CancellationToken            ct = default
    ) {
        return StartProcessInstanceCoreAsync(definitionName, variables,
                                             string.IsNullOrWhiteSpace(displayName) ? null : displayName,
                                             string.IsNullOrWhiteSpace(description) ? null : description,
                                             principal, ct);
    }

    /// <summary>Adds an already-materialised process to the cache without raising lifecycle events.</summary>
    public void Hydrate(SchemataProcess process) {
        if (!string.IsNullOrEmpty(process.CanonicalName)) {
            _instances[process.CanonicalName] = process;
        }
    }

    /// <summary>Removes a process from the cache without raising lifecycle events.</summary>
    public bool Evict(string canonicalName) {
        return _instances.TryRemove(canonicalName, out var _);
    }

    private async ValueTask<SchemataProcess> StartProcessInstanceCoreAsync(
        string                                processName,
        IReadOnlyDictionary<string, object?>? variables,
        string?                               displayName,
        string?                               description,
        ClaimsPrincipal?                      principal,
        CancellationToken                     ct
    ) {
        var reg = _registry.GetRegistration(processName)
               ?? throw new NotFoundException(message: $"Process definition '{processName}' not found.");

        var runtime = _services.GetKeyedService<IFlowRuntime>(reg.Engine)
                   ?? throw new NotFoundException(message: $"Runtime '{reg.Engine}' not found.");

        var process = new SchemataProcess {
            Name           = Guid.NewGuid().ToString("n"),
            DefinitionName = processName,
            Variables      = variables is not null ? VariableSerializer.Serialize(variables) : null,
            DisplayName    = displayName,
            Description    = description,
        };

        // Pattern from [CanonicalName("processes/{process}")] on SchemataProcess;
        // AdviceAddCanonicalName is bypassed since no repository is involved.
        process.CanonicalName = $"processes/{process.Name}";

        // Transition observers may schedule side effects that look up _instances; populate it first.
        _instances[process.CanonicalName] = process;

        SchemataProcessTransition transition;
        try {
            (_, transition) = await ApplyAsync(process, reg.Definition, "Start", null, principal,
                                               c => runtime.StartAsync(reg.Definition, process, c), ct);
        } catch {
            _instances.TryRemove(process.CanonicalName, out var _);
            throw;
        }

        await NotifyStartedAsync(process, ct);
        await NotifyTransitionedAsync(process, transition, ct);

        return process;
    }

    private (SchemataProcess process, ProcessDefinition definition, IFlowRuntime runtime) Load(string instanceName) {
        if (!_instances.TryGetValue(instanceName, out var process)) {
            throw new NotFoundException(message: $"Process instance '{instanceName}' not found.");
        }

        var reg = _registry.GetRegistration(process.DefinitionName)
               ?? throw new NotFoundException(message: $"Process definition '{process.DefinitionName}' not found.");

        var runtime = _services.GetKeyedService<IFlowRuntime>(reg.Engine)
                   ?? throw new NotFoundException(message: $"Runtime '{reg.Engine}' not found.");

        return (process, reg.Definition, runtime);
    }

    private async ValueTask<(ProcessInstance instance, SchemataProcessTransition transition)> ApplyAsync(
        SchemataProcess                                     process,
        ProcessDefinition?                                  definition,
        string                                              eventName,
        IEventDefinition?                                   trigger,
        ClaimsPrincipal?                                    principal,
        Func<CancellationToken, ValueTask<ProcessInstance>> driver,
        CancellationToken                                   ct
    ) {
        var previousState       = process.State;
        var previousWaitingAtId = process.WaitingAtId;
        var previousWaitingAt   = process.WaitingAt;
        var instance            = await driver(ct);

        process.StateId     = instance.StateId;
        process.State       = instance.State;
        process.WaitingAtId = instance.WaitingAtId;
        process.WaitingAt   = instance.WaitingAt;
        process.Variables   = VariableSerializer.Serialize(instance.Variables);

        var transitionCtx = new FlowTransitionContext {
            Process             = process,
            Definition          = definition,
            Instance            = instance,
            PreviousState       = previousState,
            PreviousWaitingAtId = previousWaitingAtId,
            PreviousWaitingAt   = previousWaitingAt,
            Trigger             = trigger,
        };

        await NotifyFlowTransitionObserversAsync(transitionCtx, ct);

        var transition = CreateTransition(process.CanonicalName!, previousState, instance.State, eventName, principal);

        return (instance, transition);
    }

    private async Task NotifyFlowTransitionObserversAsync(FlowTransitionContext context, CancellationToken ct) {
        using var scope     = _services.CreateScope();
        var       observers = scope.ServiceProvider.GetServices<IFlowTransitionObserver>().ToList();

        foreach (var observer in observers) {
            try {
                await observer.OnTransitionedAsync(context, ct);
            } catch (Exception ex) {
                _logger?.LogWarning(ex,
                                    "IFlowTransitionObserver.OnTransitionedAsync threw for process '{Name}'.",
                                    context.Process.CanonicalName);
            }
        }
    }

    private async Task NotifyStartedAsync(SchemataProcess process, CancellationToken ct) {
        using var scope     = _services.CreateScope();
        var       observers = scope.ServiceProvider.GetServices<IProcessLifecycleObserver>().ToList();

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
        SchemataProcess           process,
        SchemataProcessTransition transition,
        CancellationToken         ct
    ) {
        using var scope     = _services.CreateScope();
        var       observers = scope.ServiceProvider.GetServices<IProcessLifecycleObserver>().ToList();

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

    private async Task NotifyTerminatedAsync(SchemataProcess process, CancellationToken ct) {
        using var scope     = _services.CreateScope();
        var       observers = scope.ServiceProvider.GetServices<IProcessLifecycleObserver>().ToList();

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

    private static bool MatchesSignal(ProcessDefinition definition, SchemataProcess process, string signalName) {
        var waiting = process.WaitingAtId ?? process.StateId;
        var element = definition.Elements.FirstOrDefault(e => e.Id == waiting);

        if (element is EventBasedGateway gateway) {
            foreach (var flow in definition.Flows.Where(sf => sf.Source == gateway)) {
                if (flow.Target is FlowEvent { Position: EventPosition.IntermediateCatch } evt
                 && evt.Definition is Signal sig
                 && sig.Name == signalName) {
                    return true;
                }
            }

            return false;
        }

        return element is FlowEvent { Position: EventPosition.IntermediateCatch } intermediate
            && intermediate.Definition is Signal cur
            && cur.Name == signalName;
    }

    private static SchemataProcessTransition CreateTransition(
        string           instanceName,
        string?          previousState,
        string?          posteriorState,
        string           eventName,
        ClaimsPrincipal? principal
    ) {
        return new() {
            Name      = Guid.NewGuid().ToString("n"),
            Process   = instanceName,
            Previous  = previousState,
            Posterior = posteriorState,
            Event     = eventName,
            UpdatedBy = ResolveUpdatedBy(principal),
        };
    }

    private static string? ResolveUpdatedBy(ClaimsPrincipal? principal) {
        if (principal is null) {
            return null;
        }

        var sub = principal.FindFirst(SchemataConstants.Claims.Subject)?.Value;
        if (!string.IsNullOrWhiteSpace(sub)) {
            return $"users/{sub}";
        }

        return principal.Identity?.Name;
    }
}
