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

public sealed class ProcessRuntime : IProcessRuntime
{
    private readonly ConcurrentDictionary<string, SchemataProcess> _instances = new();
    private readonly ILogger<ProcessRuntime>?                      _logger;
    private readonly ProcessPersistence                            _persistence;
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
        _persistence = new(services);
    }

    #region IProcessRuntime Members

    public async ValueTask<SchemataProcess> StartProcessInstanceAsync(
        string                                processName,
        IReadOnlyDictionary<string, object?>? variables = null,
        ClaimsPrincipal?                      principal = null,
        CancellationToken                     ct        = default
    ) {
        var reg = _registry.GetRegistration(processName)
               ?? throw new NotFoundException(message: $"Process definition '{processName}' not found.");

        var runtime = _services.GetKeyedService<IFlowRuntime>(reg.Engine)
                   ?? throw new NotFoundException(message: $"Runtime '{reg.Engine}' not found.");

        var process = new SchemataProcess {
            Name           = Guid.NewGuid().ToString("n"),
            DefinitionName = processName,
            Variables      = variables is not null ? VariableSerializer.Serialize(variables) : null,
        };

        // Pattern from [CanonicalName("processes/{process}")] on SchemataProcess;
        // AdviceAddCanonicalName is bypassed since no repository is involved.
        process.CanonicalName = $"processes/{process.Name}";

        SchemataProcessTransition transition;
        (_, transition) = await ApplyAsync(process, reg.Definition, "Start", null, principal,
                                           c => runtime.StartAsync(reg.Definition, process, c), ct);

        _instances[process.CanonicalName] = process;

        await NotifyStartedAsync(process, ct);
        await NotifyTransitionedAsync(process, transition, ct);

        return process;
    }

    public async ValueTask<ProcessInstance> CompleteActivityAsync(
        string                                instanceName,
        IReadOnlyDictionary<string, object?>? variables = null,
        ClaimsPrincipal?                      principal = null,
        CancellationToken                     ct        = default
    ) {
        var (process, definition, runtime) = await LoadAsync(instanceName, ct);
        var previousVariables = process.Variables;

        if (variables?.Count > 0) {
            var merged = string.IsNullOrEmpty(process.Variables)
                ? new()
                : VariableSerializer.Deserialize(process.Variables!);

            foreach (var kv in variables) {
                merged[kv.Key] = kv.Value;
            }

            process.Variables = VariableSerializer.Serialize(merged);
        }

        ProcessInstance instance;
        SchemataProcessTransition transition;
        try {
            (instance, transition) = await ApplyAsync(process, definition, "CompleteActivity", null, principal,
                                                      c => runtime.AdvanceAsync(definition, process, c), ct);
        } catch {
            process.Variables = previousVariables;
            throw;
        }

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
        var (process, definition, runtime) = await LoadAsync(instanceName, ct);

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
        var candidates = new Dictionary<string, SchemataProcess>(StringComparer.Ordinal);
        foreach (var process in _instances.Values) {
            if (!string.IsNullOrEmpty(process.CanonicalName)) {
                candidates[process.CanonicalName] = process;
            }
        }

        await foreach (var process in _persistence.ListWaitingAsync(ct)) {
            if (!string.IsNullOrEmpty(process.CanonicalName) && !candidates.ContainsKey(process.CanonicalName)) {
                candidates.Add(process.CanonicalName, process);
            }
        }

        foreach (var process in candidates.Values) {
            if (process.WaitingAtId is null) continue;

            var reg = _registry.GetRegistration(process.DefinitionName);
            if (reg is null) continue;

            var runtime = _services.GetKeyedService<IFlowRuntime>(reg.Engine);
            if (runtime is null) continue;

            var signal = reg.Definition.Signals.FirstOrDefault(s => s.Name == signalName);
            if (signal is null) continue;

            if (!MatchesSignal(reg.Definition, process, signalName)) continue;

            Hydrate(process);

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
        var (process, definition, runtime) = await LoadAsync(instanceName, ct);

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
        var (process, definition, _) = await LoadAsync(instanceName, ct);

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

    private async ValueTask<(SchemataProcess process, ProcessDefinition definition, IFlowRuntime runtime)> LoadAsync(
        string            instanceName,
        CancellationToken ct
    ) {
        if (!_instances.TryGetValue(instanceName, out var process)) {
            process = await _persistence.FindAsync(instanceName, ct)
                   ?? throw new NotFoundException(message: $"Process instance '{instanceName}' not found.");

            Hydrate(process);
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
        var persisted           = CloneProcess(process);

        persisted.StateId     = instance.StateId;
        persisted.State       = instance.State;
        persisted.WaitingAtId = instance.WaitingAtId;
        persisted.WaitingAt   = instance.WaitingAt;
        persisted.Variables   = VariableSerializer.Serialize(instance.Variables);

        var transition = CreateTransition(persisted.CanonicalName!, previousState, instance.State, eventName, principal);

        await _persistence.PersistTransitionAsync(persisted, transition, ct);

        ProcessPersistence.SyncProcessFields(process, persisted);

        await NotifyFlowTransitionObserversAsync(new() {
            Process             = process,
            Definition          = definition,
            Instance            = instance,
            PreviousState       = previousState,
            PreviousWaitingAtId = previousWaitingAtId,
            PreviousWaitingAt   = previousWaitingAt,
            Trigger             = trigger,
        }, ct);

        return (instance, transition);
    }

    private static SchemataProcess CloneProcess(SchemataProcess source) {
        return new() {
            Uid            = source.Uid,
            Name           = source.Name,
            CanonicalName  = source.CanonicalName,
            DefinitionName = source.DefinitionName,
            Variables      = source.Variables,
            StateId        = source.StateId,
            State          = source.State,
            WaitingAtId    = source.WaitingAtId,
            WaitingAt      = source.WaitingAt,
            DisplayName    = source.DisplayName,
            DisplayNames   = source.DisplayNames,
            Description    = source.Description,
            Descriptions   = source.Descriptions,
            Timestamp      = source.Timestamp,
            CreateTime     = source.CreateTime,
            UpdateTime     = source.UpdateTime,
            DeleteTime     = source.DeleteTime,
            PurgeTime      = source.PurgeTime,
        };
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
