using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using ProtoBuf.Grpc;
using Schemata.Abstractions.Resource;
using Schemata.Entity.Repository;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Skeleton.Utilities;

namespace Schemata.Flow.Grpc.Services;

/// <summary>gRPC <see cref="IProcessService" /> implementation.</summary>
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

        return await _runtime.StartProcessInstanceAsync(request.DefinitionName, request.DisplayName,
                                                        request.Description, variables,
                                                        _httpContextAccessor.HttpContext?.User,
                                                        context.CancellationToken);
    }

    public async ValueTask<ProcessInstance> CompleteActivityAsync(
        CompleteActivityRequest request,
        CallContext             context = default
    ) {
        var variables = request.Variables is not null ? VariableSerializer.Deserialize(request.Variables) : null;

        return await _runtime.CompleteActivityAsync(request.CanonicalName!, variables,
                                                    _httpContextAccessor.HttpContext?.User, context.CancellationToken);
    }

    public async ValueTask<ProcessInstance> CorrelateMessageAsync(
        CorrelateMessageRequest request,
        CallContext             context = default
    ) {
        var payload = request.Payload is not null ? VariableSerializer.Deserialize(request.Payload) : null;

        return await _runtime.CorrelateMessageAsync(request.CanonicalName!, request.MessageName, payload,
                                                    _httpContextAccessor.HttpContext?.User, context.CancellationToken);
    }

    public async ValueTask ThrowSignalAsync(ThrowSignalRequest request, CallContext context = default) {
        var payload = request.Payload is not null ? VariableSerializer.Deserialize(request.Payload) : null;

        await _runtime.ThrowSignalAsync(request.SignalName, payload, _httpContextAccessor.HttpContext?.User,
                                        context.CancellationToken);
    }

    public async ValueTask<ProcessInstance> TerminateProcessInstanceAsync(
        GetRequest  request,
        CallContext context = default
    ) {
        return await _runtime.TerminateProcessInstanceAsync(request.CanonicalName!,
                                                            _httpContextAccessor.HttpContext?.User,
                                                            context.CancellationToken);
    }

    public async ValueTask<SchemataProcess?> GetProcessInstanceAsync(
        GetRequest  request,
        CallContext context = default
    ) {
        return await _processes.SingleOrDefaultAsync(q => q.Where(p => p.CanonicalName == request.CanonicalName),
                                                     context.CancellationToken);
    }

    public async ValueTask<ListResultBase<SchemataProcess>> ListProcessInstancesAsync(
        ListRequest request,
        CallContext context = default
    ) {
        var items = new List<SchemataProcess>();
        await foreach (var item in _processes.ListAsync<SchemataProcess>(null, context.CancellationToken)) {
            items.Add(item);
        }

        return new() { Entities = items };
    }

    public async ValueTask<SchemataProcessTransition?> GetProcessInstanceTransitionAsync(
        GetRequest  request,
        CallContext context = default
    ) {
        return await _transitions.SingleOrDefaultAsync(q => q.Where(t => t.CanonicalName == request.CanonicalName),
                                                       context.CancellationToken);
    }

    public async ValueTask<ListResultBase<SchemataProcessTransition>> ListProcessInstanceTransitionsAsync(
        ListRequest request,
        CallContext context = default
    ) {
        var items = new List<SchemataProcessTransition>();
        await foreach (var item in _transitions.ListAsync<SchemataProcessTransition>(
                           q => q.Where(t => t.Process == request.Parent),
                           context.CancellationToken
                           )) {
            items.Add(item);
        }

        return new() { Entities = items };
    }

    public ValueTask<ListResultBase<ProcessDefinitionInfo>> ListProcessDefinitionsAsync(
        ListRequest request,
        CallContext context = default
    ) {
        var entities = _registry.GetRegisteredProcesses()
                                .Select(n => new ProcessDefinitionInfo { CanonicalName = $"definitions/{n}" })
                                .ToList();
        return new(new ListResultBase<ProcessDefinitionInfo> { Entities = entities });
    }

    #endregion
}
