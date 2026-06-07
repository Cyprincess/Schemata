using System.Threading.Tasks;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Configuration;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Grpc.Services;

/// <summary>
///     gRPC service contract for process flow operations. Bound explicitly by
///     <c>SchemataFlowGrpcFeature.ConfigureEndpoints</c>.
/// </summary>
public interface IProcessService
{
    /// <summary>Starts a new process instance from the specified definition.</summary>
    [Operation]
    ValueTask<SchemataProcess> StartProcessInstanceAsync(
        StartProcessInstanceRequest request,
        CallContext                 context = default
    );

    /// <summary>Completes the current activity and auto-advances the instance.</summary>
    [Operation]
    ValueTask<ProcessInstance> CompleteActivityAsync(CompleteActivityRequest request, CallContext context = default);

    /// <summary>Correlates a named message to a specific process instance.</summary>
    [Operation]
    ValueTask<ProcessInstance> CorrelateMessageAsync(CorrelateMessageRequest request, CallContext context = default);

    /// <summary>Throws (broadcasts) a signal to all waiting process instances.</summary>
    [Operation]
    ValueTask ThrowSignalAsync(ThrowSignalRequest request, CallContext context = default);

    /// <summary>Terminates a process instance immediately (lookup by canonical name).</summary>
    [Operation]
    ValueTask<ProcessInstance> TerminateProcessInstanceAsync(GetRequest request, CallContext context = default);

    /// <summary>Gets a single process instance by canonical name (AIP-131).</summary>
    [Operation]
    ValueTask<SchemataProcess?> GetProcessInstanceAsync(GetRequest request, CallContext context = default);

    /// <summary>Lists process instances (AIP-132).</summary>
    [Operation]
    ValueTask<ListResultBase<SchemataProcess>> ListProcessInstancesAsync(
        ListRequest request,
        CallContext context = default
    );

    /// <summary>Gets a single transition record by canonical name (AIP-131).</summary>
    [Operation]
    ValueTask<SchemataProcessTransition?> GetProcessInstanceTransitionAsync(
        GetRequest  request,
        CallContext context = default
    );

    /// <summary>Lists transitions of a process instance (AIP-132).</summary>
    [Operation]
    ValueTask<ListResultBase<SchemataProcessTransition>> ListProcessInstanceTransitionsAsync(
        ListRequest request,
        CallContext context = default
    );

    /// <summary>Lists registered process definitions (AIP-132).</summary>
    [Operation]
    ValueTask<ListResultBase<ProcessDefinitionInfo>> ListProcessDefinitionsAsync(
        ListRequest request,
        CallContext context = default
    );
}
