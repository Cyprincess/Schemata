using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Schemata.Abstractions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Common;
using Schemata.Common.Errors;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Skeleton.Utilities;

namespace Schemata.Flow.Foundation;

/// <summary>Coordinates process runtime operations against registered Flow engines.</summary>
public sealed partial class ProcessRuntime : IProcessRuntime
{
    private readonly ConcurrentDictionary<string, SchemataProcess> _instances = new();
    private readonly ILogger<ProcessRuntime>?                      _logger;
    private readonly ProcessPersistence                            _persistence;
    private readonly IProcessRegistry                              _registry;
    private readonly IServiceProvider                              _services;

    /// <summary>Creates a process runtime backed by the registered Flow engine registry.</summary>
    public ProcessRuntime(
        IProcessRegistry         registry,
        IServiceProvider         services,
        ILogger<ProcessRuntime>? logger = null
    ) {
        _registry = registry;
        _services = services;
        _logger   = logger;
        _persistence = new();
    }

    private async ValueTask<(SchemataProcess process, ProcessDefinition definition, IFlowRuntime runtime)> LoadAsync(
        IServiceProvider  services,
        string            instanceName,
        CancellationToken ct
    ) {
        if (!_instances.TryGetValue(instanceName, out var process)) {
            process = await _persistence.FindAsync(services, instanceName, ct)
                   ?? throw SchemataResourceErrors.NotFound<SchemataProcess>($"processes/{instanceName}");

            Hydrate(process);
        }

        var reg = _registry.GetRegistration(process.DefinitionName)
               ?? throw new NotFoundException(
                   reason: "PROCESS_DEFINITION_NOT_REGISTERED",
                   message: $"Process definition '{process.DefinitionName}' not registered.");

        var runtime = services.GetKeyedService<IFlowRuntime>(reg.Engine)
                   ?? throw new FailedPreconditionException(
                       reason: "FLOW_RUNTIME_NOT_REGISTERED",
                       message: $"Flow runtime '{reg.Engine}' is not registered with the host.");

        return (process, reg.Definition, runtime);
    }

    private async ValueTask<(ProcessInstance instance, SchemataProcessTransition transition)> ApplyAsync(
        IServiceProvider                                    services,
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

        var transition = CreateTransition(persisted.Name!, previousState, instance.State, eventName, principal);

        var context = new FlowTransitionContext {
            Process             = process,
            Definition          = definition,
            Instance            = instance,
            PreviousState       = previousState,
            PreviousWaitingAtId = previousWaitingAtId,
            PreviousWaitingAt   = previousWaitingAt,
            Trigger             = trigger,
        };

        // Provision the wake-up infrastructure (timer jobs, event subscriptions) the new waiting state
        // depends on before committing the transition. A provisioning failure aborts the transition
        // before persistence can strand the instance.
        await ProvisionFlowTransitionAsync(services, context, ct);

        var engine    = _registry.GetRegistration(persisted.DefinitionName)?.Engine;
        var writeback = ProcessWriteback.Build(services, persisted, instance, engine);

        await _persistence.PersistTransitionAsync(services, persisted, transition, writeback, ct);

        ProcessPersistence.SyncProcessFields(process, persisted);

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
            SourceType    = source.SourceType,
            Source = source.Source,
            SourceTimestamp     = source.SourceTimestamp,
            Timestamp      = source.Timestamp,
            CreateTime     = source.CreateTime,
            UpdateTime     = source.UpdateTime,
            DeleteTime     = source.DeleteTime,
            PurgeTime      = source.PurgeTime,
        };
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

    private static void CaptureSource(SchemataProcess process, object? sourceEntity) {
        if (sourceEntity is null) {
            return;
        }

        process.SourceType    = sourceEntity.GetType().FullName;
        process.Source = sourceEntity is ICanonicalName named ? named.CanonicalName : null;
        process.SourceTimestamp     = sourceEntity is IConcurrency stamped ? stamped.Timestamp : null;
    }

    private static SchemataProcessTransition CreateTransition(
        string           processName,
        string?          previousState,
        string?          posteriorState,
        string           eventName,
        ClaimsPrincipal? principal
    ) {
        return new() {
            Name      = Identifiers.NewUid().ToString("n"),
            Process   = processName,
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
