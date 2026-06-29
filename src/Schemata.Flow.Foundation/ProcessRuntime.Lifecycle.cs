using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Common;
using Schemata.Event.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Skeleton.Utilities;

namespace Schemata.Flow.Foundation;

/// <summary>Coordinates process runtime operations against registered Flow engines.</summary>
public sealed partial class ProcessRuntime
{
    public async ValueTask<SchemataProcess> StartProcessInstanceAsync(
        string                                processName,
        IReadOnlyDictionary<string, object?>? variables   = null,
        ClaimsPrincipal?                      principal   = null,
        string?                               displayName = null,
        string?                               description = null,
        object?                               sourceEntity = null,
        CancellationToken                     ct          = default
    ) {
        if (sourceEntity is not null) {
            EventSourceContract.Ensure(sourceEntity);
        }

        var reg = _registry.GetRegistration(processName)
               ?? throw new NotFoundException(
                   SchemataResources.PROCESS_NOT_REGISTERED,
                   new Dictionary<string, string> { ["name"] = processName });

        // One scope spans the whole operation so persistence, provisioning, and lifecycle
        // notifications resolve their scoped services (repositories, advisors) from a single scope.
        using var scope = _services.CreateScope();
        var       sp    = scope.ServiceProvider;

        var runtime = sp.GetKeyedService<IFlowRuntime>(reg.Engine)
                   ?? throw new FailedPreconditionException(
                       SchemataResources.FLOW_RUNTIME_NOT_REGISTERED,
                       new Dictionary<string, string> { ["engine"] = reg.Engine });

        var process = new SchemataProcess {
            Name           = Identifiers.NewUid().ToString("n"),
            DefinitionName = processName,
            Variables      = variables is not null ? VariableSerializer.Serialize(variables) : null,
            DisplayName    = string.IsNullOrWhiteSpace(displayName) ? null : displayName,
            Description    = string.IsNullOrWhiteSpace(description) ? null : description,
        };

        CaptureSource(process, sourceEntity);

        // Pattern from [CanonicalName("processes/{process}")] on SchemataProcess;
        // this path bypasses repository advice.
        process.CanonicalName = $"processes/{process.Name}";

        SchemataProcessTransition transition;
        try {
            (_, transition) = await ApplyAsync(sp, process, reg.Definition, "Start", null, principal,
                                               c => runtime.StartAsync(reg.Definition, process, c), ct);
        } catch (Exception ex) {
            await PublishFailedAsync(sp, process, ex, ct);
            throw;
        }

        _instances[process.CanonicalName] = process;

        await NotifyStartedAsync(sp, process, ct);
        await NotifyTransitionedAsync(sp, process, transition, ct);

        return process;
    }

    public async ValueTask<ProcessInstance> CompleteActivityAsync(
        string                                instanceName,
        IReadOnlyDictionary<string, object?>? variables = null,
        ClaimsPrincipal?                      principal = null,
        CancellationToken                     ct        = default
    ) {
        using var scope = _services.CreateScope();
        var       sp    = scope.ServiceProvider;

        var (process, definition, runtime) = await LoadAsync(sp, instanceName, ct);
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
            (instance, transition) = await ApplyAsync(sp, process, definition, "CompleteActivity", null, principal,
                                                      c => runtime.AdvanceAsync(definition, process, c), ct);
        } catch (Exception ex) {
            process.Variables = previousVariables;
            await PublishFailedAsync(sp, process, ex, ct);
            throw;
        }

        await NotifyTransitionedAsync(sp, process, transition, ct);

        if (instance.IsComplete) {
            _instances.TryRemove(process.CanonicalName!, out var _);
            await NotifyTerminatedAsync(sp, process, ct);
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
        using var scope = _services.CreateScope();
        var       sp    = scope.ServiceProvider;

        var (process, definition, runtime) = await LoadAsync(sp, instanceName, ct);

        var msg = definition.Messages.FirstOrDefault(m => m.Name == messageName)
               ?? throw new InvalidArgumentException(
                   SchemataResources.PROCESS_MESSAGE_NOT_DEFINED,
                   new Dictionary<string, string> { ["name"] = messageName });

        ProcessInstance instance;
        SchemataProcessTransition transition;
        try {
            (instance, transition) = await ApplyAsync(sp, process, definition, messageName, msg, principal,
                                                      c => runtime.TriggerAsync(definition, process, msg, payload, c), ct);
        } catch (Exception ex) {
            await PublishFailedAsync(sp, process, ex, ct);
            throw;
        }

        await NotifyTransitionedAsync(sp, process, transition, ct);

        if (instance.IsComplete) {
            _instances.TryRemove(process.CanonicalName!, out var _);
            await NotifyTerminatedAsync(sp, process, ct);
        }

        return instance;
    }

    public async ValueTask ThrowSignalAsync(
        string            signalName,
        object?           payload   = null,
        ClaimsPrincipal?  principal = null,
        CancellationToken ct        = default
    ) {
        using var scope = _services.CreateScope();
        var       sp    = scope.ServiceProvider;

        var candidates = new Dictionary<string, SchemataProcess>(StringComparer.Ordinal);
        foreach (var process in _instances.Values) {
            if (!string.IsNullOrEmpty(process.CanonicalName)) {
                candidates[process.CanonicalName] = process;
            }
        }

        await foreach (var process in _persistence.ListWaitingAsync(sp, ct)) {
            if (!string.IsNullOrEmpty(process.CanonicalName) && !candidates.ContainsKey(process.CanonicalName)) {
                candidates.Add(process.CanonicalName, process);
            }
        }

        foreach (var process in candidates.Values) {
            if (process.WaitingAtId is null) continue;

            var reg = _registry.GetRegistration(process.DefinitionName);
            if (reg is null) continue;

            var runtime = sp.GetKeyedService<IFlowRuntime>(reg.Engine);
            if (runtime is null) continue;

            var signal = reg.Definition.Signals.FirstOrDefault(s => s.Name == signalName);
            if (signal is null) continue;

            if (!MatchesSignal(reg.Definition, process, signalName)) continue;

            Hydrate(process);

            ProcessInstance instance;
            SchemataProcessTransition transition;
            try {
                (instance, transition) = await ApplyAsync(sp, process, reg.Definition, signalName, signal, principal,
                                                          c => runtime.TriggerAsync(reg.Definition, process, signal, payload, c), ct);
            } catch (Exception ex) {
                await PublishFailedAsync(sp, process, ex, ct);
                throw;
            }

            await NotifyTransitionedAsync(sp, process, transition, ct);

            if (instance.IsComplete) {
                _instances.TryRemove(process.CanonicalName!, out var _);
                await NotifyTerminatedAsync(sp, process, ct);
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
        using var scope = _services.CreateScope();
        var       sp    = scope.ServiceProvider;

        var (process, definition, runtime) = await LoadAsync(sp, instanceName, ct);

        ProcessInstance instance;
        SchemataProcessTransition transition;
        try {
            (instance, transition) = await ApplyAsync(sp, process, definition, trigger.Name, trigger, principal,
                                                      c => runtime.TriggerAsync(definition, process, trigger, payload, c), ct);
        } catch (Exception ex) {
            await PublishFailedAsync(sp, process, ex, ct);
            throw;
        }

        await NotifyTransitionedAsync(sp, process, transition, ct);

        if (instance.IsComplete) {
            _instances.TryRemove(process.CanonicalName!, out var _);
            await NotifyTerminatedAsync(sp, process, ct);
        }

        return instance;
    }

    public async ValueTask<ProcessInstance> TerminateProcessInstanceAsync(
        string            instanceName,
        ClaimsPrincipal?  principal = null,
        CancellationToken ct        = default
    ) {
        using var scope = _services.CreateScope();
        var       sp    = scope.ServiceProvider;

        var (process, definition, _) = await LoadAsync(sp, instanceName, ct);

        ProcessInstance instance;
        SchemataProcessTransition transition;
        try {
            (instance, transition) = await ApplyAsync(sp, process, definition, "Terminate", null, principal,
                                                      _ => ValueTask.FromResult(new ProcessInstance {
                                                          StateId    = "terminated",
                                                          State      = "Terminated",
                                                          IsComplete = true,
                                                          Variables = string.IsNullOrEmpty(process.Variables)
                                                              ? new()
                                                              : VariableSerializer.Deserialize(process.Variables!),
                                                      }), ct);
        } catch (Exception ex) {
            await PublishFailedAsync(sp, process, ex, ct);
            throw;
        }

        _instances.TryRemove(instanceName, out var _);

        await NotifyTransitionedAsync(sp, process, transition, ct);
        await NotifyTerminatedAsync(sp, process, ct);

        return instance;
    }
}
