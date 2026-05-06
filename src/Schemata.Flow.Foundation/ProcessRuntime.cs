using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Skeleton.Utilities;

namespace Schemata.Flow.Foundation;

public sealed class ProcessRuntime : IProcessRuntime
{
    private readonly IRepository<SchemataProcess>           _processes;
    private readonly IProcessRegistry                       _registry;
    private readonly IServiceProvider                       _services;
    private readonly IRepository<SchemataProcessTransition> _transitions;

    public ProcessRuntime(
        IRepository<SchemataProcess>           processes,
        IRepository<SchemataProcessTransition> transitions,
        IProcessRegistry                       registry,
        IServiceProvider                       services
    ) {
        _processes   = processes;
        _transitions = transitions;
        _registry    = registry;
        _services    = services;
    }

    #region IProcessRuntime Members

    public async ValueTask<SchemataProcess> StartProcessInstanceAsync(
        string                                processName,
        IReadOnlyDictionary<string, object?>? variables,
        ClaimsPrincipal?                      principal,
        CancellationToken                     ct = default
    ) {
        var reg = _registry.GetRegistration(processName)
               ?? throw new NotFoundException(message: $"Process definition '{processName}' not found.");

        var runtime = _services.GetKeyedService<IFlowRuntime>(reg.Engine)
                   ?? throw new NotFoundException(message: $"Runtime '{reg.Engine}' not found.");

        using var uow = _processes.BeginWork();

        var process = new SchemataProcess {
            Name                  = Guid.NewGuid().ToString("n"),
            ProcessDefinitionName = processName,
            Variables             = variables is not null ? VariableSerializer.Serialize(variables) : null,
        };

        var instance = await runtime.StartAsync(reg.Definition, process, ct);
        process.StateId     = instance.StateId;
        process.State       = instance.State;
        process.WaitingAtId = instance.WaitingAtId;
        process.WaitingAt   = instance.WaitingAt;
        process.Variables   = VariableSerializer.Serialize(instance.Variables);

        await _processes.AddAsync(process, ct);
        await uow.CommitAsync(ct);

        return process;
    }

    public async ValueTask<ProcessInstance> CompleteActivityAsync(
        string                                instanceName,
        IReadOnlyDictionary<string, object?>? variables,
        ClaimsPrincipal?                      principal,
        CancellationToken                     ct = default
    ) {
        using var uow = _processes.BeginWork();

        var process = await _processes.SingleOrDefaultAsync(q => q.Where(p => p.CanonicalName == instanceName), ct)
                   ?? throw new NotFoundException(message: $"Process instance '{instanceName}' not found.");

        var reg = _registry.GetRegistration(process.ProcessDefinitionName)
               ?? throw new NotFoundException(
                      message: $"Process definition '{process.ProcessDefinitionName}' not found."
                  );

        var runtime = _services.GetKeyedService<IFlowRuntime>(reg.Engine)
                   ?? throw new NotFoundException(message: $"Runtime '{reg.Engine}' not found.");

        var previousState = process.State;

        if (variables?.Count > 0) {
            var existingVars = string.IsNullOrEmpty(process.Variables)
                ? new()
                : VariableSerializer.Deserialize(process.Variables!);

            foreach (var kv in variables) {
                existingVars[kv.Key] = kv.Value;
            }

            process.Variables = VariableSerializer.Serialize(existingVars);
        }

        var instance = await runtime.AdvanceAsync(reg.Definition, process, ct);

        process.StateId     = instance.StateId;
        process.State       = instance.State;
        process.WaitingAtId = instance.WaitingAtId;
        process.WaitingAt   = instance.WaitingAt;

        await _processes.UpdateAsync(process, ct);

        var transition = CreateTransition(instanceName, previousState, instance.State, "CompleteActivity", principal);

        await _transitions.AddAsync(transition, ct);
        await uow.CommitAsync(ct);

        return instance;
    }

    public async ValueTask<ProcessInstance> CorrelateMessageAsync(
        string            instanceName,
        string            messageName,
        object?           payload,
        ClaimsPrincipal?  principal,
        CancellationToken ct = default
    ) {
        using var uow = _processes.BeginWork();

        var process = await _processes.SingleOrDefaultAsync(q => q.Where(p => p.CanonicalName == instanceName), ct)
                   ?? throw new NotFoundException(message: $"Process instance '{instanceName}' not found.");

        var reg = _registry.GetRegistration(process.ProcessDefinitionName)
               ?? throw new NotFoundException(
                      message: $"Process definition '{process.ProcessDefinitionName}' not found."
                  );

        var runtime = _services.GetKeyedService<IFlowRuntime>(reg.Engine)
                   ?? throw new NotFoundException(message: $"Runtime '{reg.Engine}' not found.");

        var msg = reg.Definition.Messages.FirstOrDefault(m => m.Name == messageName);
        if (msg is null) {
            throw new NotFoundException(message: $"Message '{messageName}' not found in process definition.");
        }

        var previousState = process.State;

        var instance = await runtime.TriggerAsync(reg.Definition, process, msg, payload, ct);

        process.StateId     = instance.StateId;
        process.State       = instance.State;
        process.WaitingAtId = instance.WaitingAtId;
        process.WaitingAt   = instance.WaitingAt;
        process.Variables   = VariableSerializer.Serialize(instance.Variables);

        await _processes.UpdateAsync(process, ct);

        var transition = CreateTransition(instanceName, previousState, instance.State, messageName, principal);

        await _transitions.AddAsync(transition, ct);
        await uow.CommitAsync(ct);

        return instance;
    }

    public async ValueTask ThrowSignalAsync(
        string            signalName,
        object?           payload,
        ClaimsPrincipal?  principal,
        CancellationToken ct = default
    ) {
        using var uow = _processes.BeginWork();

        await foreach (var process in _processes.ListAsync<SchemataProcess>(null, ct)) {
            var reg = _registry.GetRegistration(process.ProcessDefinitionName);
            if (reg is null) continue;

            var runtime = _services.GetKeyedService<IFlowRuntime>(reg.Engine);
            if (runtime is null) continue;

            var signal = reg.Definition.Signals.FirstOrDefault(s => s.Name == signalName);
            if (signal is null) continue;

            var waiting = process.WaitingAtId ?? process.StateId;
            var element   = reg.Definition.Elements.FirstOrDefault(e => e.Id == waiting);

            var matches = false;
            if (element is EventBasedGateway gateway) {
                var outgoing = reg.Definition.Flows.Where(sf => sf.Source == gateway).ToList();
                foreach (var flow in outgoing) {
                    if (flow.Target is FlowEvent evt && evt.Position == EventPosition.IntermediateCatch) {
                        if (evt.Definition is Signal sig && sig.Name == signalName) {
                            matches = true;
                            break;
                        }
                    }
                }
            } else if (element is FlowEvent evt && evt.Position == EventPosition.IntermediateCatch) {
                if (evt.Definition is Signal sig && sig.Name == signalName) {
                    matches = true;
                }
            }

            if (!matches) continue;

            var previousState = process.State;

            var instance = await runtime.TriggerAsync(reg.Definition, process, signal, payload, ct);

            process.StateId     = instance.StateId;
            process.State       = instance.State;
            process.WaitingAtId = instance.WaitingAtId;
            process.WaitingAt   = instance.WaitingAt;
            process.Variables   = VariableSerializer.Serialize(instance.Variables);

            await _processes.UpdateAsync(process, ct);

            var transition = CreateTransition(
                process.CanonicalName!, previousState, instance.State, signalName, principal);

            await _transitions.AddAsync(transition, ct);
        }

        await uow.CommitAsync(ct);
    }

    public async ValueTask<ProcessInstance> TerminateProcessInstanceAsync(
        string            instanceName,
        ClaimsPrincipal?  principal,
        CancellationToken ct = default
    ) {
        using var uow = _processes.BeginWork();

        var process = await _processes.SingleOrDefaultAsync(q => q.Where(p => p.CanonicalName == instanceName), ct)
                   ?? throw new NotFoundException(message: $"Process instance '{instanceName}' not found.");

        var previousState = process.State;

        process.State       = "Terminated";
        process.StateId     = "terminated";
        process.WaitingAt   = null;
        process.WaitingAtId = null;

        await _processes.UpdateAsync(process, ct);

        var transition = CreateTransition(instanceName, previousState, "Terminated", "Terminate", principal);

        await _transitions.AddAsync(transition, ct);
        await uow.CommitAsync(ct);

        return new() {
            StateId = process.StateId,
            State   = process.State,
            Variables = string.IsNullOrEmpty(process.Variables)
                ? new()
                : VariableSerializer.Deserialize(process.Variables!),
            IsComplete = true,
        };
    }

    #endregion

    public async ValueTask<SchemataProcess> StartProcessInstanceAsync(
        string                       definitionName,
        string?                      displayName,
        string?                      description,
        Dictionary<string, object?>? variables,
        ClaimsPrincipal?             principal,
        CancellationToken            ct = default
    ) {
        var process = await StartProcessInstanceAsync(definitionName, variables, principal, ct);

        if (!string.IsNullOrWhiteSpace(displayName) || !string.IsNullOrWhiteSpace(description)) {
            if (!string.IsNullOrWhiteSpace(displayName)) {
                process.DisplayName = displayName;
            }

            if (!string.IsNullOrWhiteSpace(description)) {
                process.Description = description;
            }

            await _processes.UpdateAsync(process, ct);
        }

        return process;
    }

    private static SchemataProcessTransition CreateTransition(
        string           instanceName,
        string?          previousState,
        string?          posteriorState,
        string           eventName,
        ClaimsPrincipal? principal
    ) {
        return new() {
            Name          = Guid.NewGuid().ToString("n"),
            ProcessName   = instanceName,
            Previous      = previousState,
            Posterior     = posteriorState,
            Event         = eventName,
            UpdatedByName = ResolveUpdatedBy(principal),
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
