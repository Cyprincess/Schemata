using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using ProtoBuf.Grpc;
using Schemata.Entity.Repository;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Skeleton.Utilities;

namespace Schemata.Flow.Grpc.Services;

/// <summary>
///     The gRPC service implementation for process flow operations using BPMN 2.0 terminology.
/// </summary>
public sealed class ProcessService : IProcessService
{
    private readonly IHttpContextAccessor                   _httpContextAccessor;
    private readonly IRepository<SchemataProcess>           _processes;
    private readonly IProcessRegistry                       _registry;
    private readonly ProcessRuntime                         _runtime;
    private readonly IRepository<SchemataProcessTransition> _transitions;

    public ProcessService(
        ProcessRuntime                         runtime,
        IRepository<SchemataProcess>           processes,
        IRepository<SchemataProcessTransition> transitions,
        IProcessRegistry                       registry,
        IHttpContextAccessor                   httpContextAccessor
    ) {
        _runtime             = runtime;
        _processes           = processes;
        _transitions         = transitions;
        _registry            = registry;
        _httpContextAccessor = httpContextAccessor;
    }

    #region IProcessService Members

    public async ValueTask<SchemataProcess> StartProcessInstanceAsync(
        StartProcessInstanceRequest request,
        CallContext                 context = default
    ) {
        var variables = request.Variables is not null ? VariableSerializer.Deserialize(request.Variables) : null;

        return await _runtime.StartProcessInstanceAsync(
            request.DefinitionName,
            request.DisplayName,
            request.Description,
            variables,
            _httpContextAccessor.HttpContext?.User,
            context.CancellationToken
        );
    }

    public async ValueTask<ProcessInstance> CompleteActivityAsync(
        CompleteActivityRequest request,
        CallContext             context = default
    ) {
        var variables = request.Variables is not null ? VariableSerializer.Deserialize(request.Variables) : null;

        return await _runtime.CompleteActivityAsync(
            request.InstanceName!,
            variables,
            _httpContextAccessor.HttpContext?.User,
            context.CancellationToken
        );
    }

    public async ValueTask<ProcessInstance> CorrelateMessageAsync(
        CorrelateMessageRequest request,
        CallContext             context = default
    ) {
        var payload = request.Payload is not null ? VariableSerializer.Deserialize(request.Payload) : null;

        return await _runtime.CorrelateMessageAsync(
            request.InstanceName!,
            request.MessageName,
            payload,
            _httpContextAccessor.HttpContext?.User,
            context.CancellationToken
        );
    }

    public async ValueTask ThrowSignalAsync(ThrowSignalRequest request, CallContext context = default) {
        var payload = request.Payload is not null ? VariableSerializer.Deserialize(request.Payload) : null;

        await _runtime.ThrowSignalAsync(
            request.SignalName,
            payload,
            _httpContextAccessor.HttpContext?.User,
            context.CancellationToken
        );
    }

    public async ValueTask<ProcessInstance> TerminateProcessInstanceAsync(
        TerminateProcessInstanceRequest request,
        CallContext                     context = default
    ) {
        return await _runtime.TerminateProcessInstanceAsync(
            request.InstanceName,
            _httpContextAccessor.HttpContext?.User,
            context.CancellationToken
        );
    }

    public async ValueTask<SchemataProcess?> GetProcessInstanceAsync(
        GetProcessInstanceRequest request,
        CallContext               context = default
    ) {
        return await _processes.SingleOrDefaultAsync(
            q => q.Where(p => p.CanonicalName == request.Name),
            context.CancellationToken
        );
    }

    public async ValueTask<ListProcessInstancesResponse> ListProcessInstancesAsync(
        ListProcessInstancesRequest request,
        CallContext                 context = default
    ) {
        var items = new List<SchemataProcess>();
        await foreach (var item in _processes.ListAsync<SchemataProcess>(null, context.CancellationToken)) {
            items.Add(item);
        }

        return new() { Processes = items };
    }

    public async ValueTask<SchemataProcessTransition?> GetProcessInstanceTransitionAsync(
        GetProcessInstanceTransitionRequest request,
        CallContext                         context = default
    ) {
        return await _transitions.SingleOrDefaultAsync(
            q => q.Where(t => t.CanonicalName == request.Name),
            context.CancellationToken
        );
    }

    public async ValueTask<ListProcessInstanceTransitionsResponse> ListProcessInstanceTransitionsAsync(
        ListProcessInstanceTransitionsRequest request,
        CallContext                           context = default
    ) {
        var items = new List<SchemataProcessTransition>();
        await foreach (var item in _transitions.ListAsync<SchemataProcessTransition>(
                           q => q.Where(t => t.ProcessName == request.ProcessName),
                           context.CancellationToken
                       )) {
            items.Add(item);
        }

        return new() { Transitions = items };
    }

    public ValueTask<ListProcessDefinitionsResponse> ListProcessDefinitionsAsync(
        ListProcessDefinitionsRequest request,
        CallContext                   context = default
    ) {
        var names = _registry.GetRegisteredProcesses();
        return new(new ListProcessDefinitionsResponse { Names = names.ToList() });
    }

    #endregion
}
