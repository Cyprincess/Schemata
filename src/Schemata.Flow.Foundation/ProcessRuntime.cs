using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Advisors;
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

    public ValueTask<SchemataProcess> StartProcessInstanceAsync(
        string                                processName,
        IReadOnlyDictionary<string, object?>? variables,
        ClaimsPrincipal?                      principal,
        CancellationToken                     ct = default
    ) {
        return StartProcessInstanceCoreAsync(processName, variables, null, null, principal, ct);
    }

    public async ValueTask<ProcessInstance> CompleteActivityAsync(
        string                                instanceName,
        IReadOnlyDictionary<string, object?>? variables,
        ClaimsPrincipal?                      principal,
        CancellationToken                     ct = default
    ) {
        using var uow = _processes.BeginWork();
        var (process, definition, runtime) = await LoadAsync(instanceName, ct);

        if (variables?.Count > 0) {
            var merged = string.IsNullOrEmpty(process.Variables)
                ? new()
                : VariableSerializer.Deserialize(process.Variables!);

            foreach (var kv in variables) {
                merged[kv.Key] = kv.Value;
            }

            process.Variables = VariableSerializer.Serialize(merged);
        }

        var instance = await ApplyTransitionAsync(process, definition, "CompleteActivity", null, principal,
                                                  c => runtime.AdvanceAsync(definition, process, c), ct);

        await _processes.UpdateAsync(process, ct);
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
        var (process, definition, runtime) = await LoadAsync(instanceName, ct);

        var msg = definition.Messages.FirstOrDefault(m => m.Name == messageName)
               ?? throw new NotFoundException(message: $"Message '{messageName}' not found in process definition.");

        var instance = await ApplyTransitionAsync(process, definition, messageName, msg, principal,
                                                  c => runtime.TriggerAsync(definition, process, msg, payload, c), ct);

        await _processes.UpdateAsync(process, ct);
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

        // Push the "still waiting" filter down to the persistence layer; completed
        // processes have no WaitingAtId and can never consume a signal. The per-row
        // signal-shape check still runs in memory against the ProcessDefinition.
        await foreach (var process in _processes.ListAsync<SchemataProcess>(
                           q => q.Where(p => p.WaitingAtId != null), ct)) {
            var reg = _registry.GetRegistration(process.DefinitionName);
            if (reg is null) continue;

            var runtime = _services.GetKeyedService<IFlowRuntime>(reg.Engine);
            if (runtime is null) continue;

            var signal = reg.Definition.Signals.FirstOrDefault(s => s.Name == signalName);
            if (signal is null) continue;

            if (!MatchesSignal(reg.Definition, process, signalName)) continue;

            await ApplyTransitionAsync(process, reg.Definition, signalName, signal, principal,
                                       c => runtime.TriggerAsync(reg.Definition, process, signal, payload, c), ct);

            await _processes.UpdateAsync(process, ct);
        }

        await uow.CommitAsync(ct);
    }

    public async ValueTask<ProcessInstance> TriggerEventAsync(
        string            instanceName,
        IEventDefinition  trigger,
        object?           payload,
        ClaimsPrincipal?  principal,
        CancellationToken ct = default
    ) {
        using var uow = _processes.BeginWork();
        var (process, definition, runtime) = await LoadAsync(instanceName, ct);

        var instance = await ApplyTransitionAsync(process, definition, trigger.Name, trigger, principal,
                                                  c => runtime.TriggerAsync(definition, process, trigger, payload, c), ct);

        await _processes.UpdateAsync(process, ct);
        await uow.CommitAsync(ct);
        return instance;
    }

    public async ValueTask<ProcessInstance> TerminateProcessInstanceAsync(
        string            instanceName,
        ClaimsPrincipal?  principal,
        CancellationToken ct = default
    ) {
        using var uow = _processes.BeginWork();

        var process = await _processes.SingleOrDefaultAsync(q => q.Where(p => p.CanonicalName == instanceName), ct)
                   ?? throw new NotFoundException(message: $"Process instance '{instanceName}' not found.");

        var definition = _registry.GetRegistration(process.DefinitionName)?.Definition;

        var instance = await ApplyTransitionAsync(process, definition, "Terminate", null, principal,
                                                  _ => ValueTask.FromResult(new ProcessInstance {
                                                      StateId    = "terminated",
                                                      State      = "Terminated",
                                                      IsComplete = true,
                                                      Variables = string.IsNullOrEmpty(process.Variables)
                                                          ? new()
                                                          : VariableSerializer.Deserialize(process.Variables!),
                                                  }), ct);

        await _processes.UpdateAsync(process, ct);
        await uow.CommitAsync(ct);
        return instance;
    }

    #endregion

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

        using var uow = _processes.BeginWork();

        var process = new SchemataProcess {
            Name           = Guid.NewGuid().ToString("n"),
            DefinitionName = processName,
            Variables      = variables is not null ? VariableSerializer.Serialize(variables) : null,
            DisplayName    = displayName,
            Description    = description,
        };

        await ApplyTransitionAsync(process, reg.Definition, "Start", null, principal,
                                   c => runtime.StartAsync(reg.Definition, process, c), ct);

        await _processes.AddAsync(process, ct);
        await uow.CommitAsync(ct);
        return process;
    }

    private async ValueTask<(SchemataProcess process, ProcessDefinition definition, IFlowRuntime runtime)> LoadAsync(
        string            instanceName,
        CancellationToken ct
    ) {
        var process = await _processes.SingleOrDefaultAsync(q => q.Where(p => p.CanonicalName == instanceName), ct)
                   ?? throw new NotFoundException(message: $"Process instance '{instanceName}' not found.");

        var reg = _registry.GetRegistration(process.DefinitionName)
               ?? throw new NotFoundException(message: $"Process definition '{process.DefinitionName}' not found.");

        var runtime = _services.GetKeyedService<IFlowRuntime>(reg.Engine)
                   ?? throw new NotFoundException(message: $"Runtime '{reg.Engine}' not found.");

        return (process, reg.Definition, runtime);
    }

    private async ValueTask<ProcessInstance> ApplyTransitionAsync(
        SchemataProcess                                     process,
        ProcessDefinition?                                  definition,
        string                                              eventName,
        IEventDefinition?                                   trigger,
        ClaimsPrincipal?                                    principal,
        Func<CancellationToken, ValueTask<ProcessInstance>> driver,
        CancellationToken                                   ct
    ) {
        var previousState = process.State;
        var instance      = await driver(ct);

        process.StateId     = instance.StateId;
        process.State       = instance.State;
        process.WaitingAtId = instance.WaitingAtId;
        process.WaitingAt   = instance.WaitingAt;
        process.Variables   = VariableSerializer.Serialize(instance.Variables);

        var advisorCtx = new AdviceContext(_services);
        var transitionCtx = new FlowTransitionContext {
            Process       = process,
            Definition    = definition,
            Instance      = instance,
            PreviousState = previousState,
            Trigger       = trigger,
        };

        _ = await Advisor.For<IFlowTransitionAdvisor>().RunAsync(advisorCtx, transitionCtx, ct);

        var transition = CreateTransition(process.CanonicalName!, previousState, instance.State, eventName, principal);
        await _transitions.AddAsync(transition, ct);

        return instance;
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
