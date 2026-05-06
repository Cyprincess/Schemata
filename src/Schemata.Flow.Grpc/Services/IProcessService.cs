using System.Threading.Tasks;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Configuration;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Grpc.Services;

/// <summary>
///     The gRPC service contract for process flow operations using BPMN 2.0 terminology.
/// </summary>
/// [Service]
public interface IProcessService
{
    /// <summary>Starts a new process instance from the specified definition.</summary>
    [Operation]
    ValueTask<SchemataProcess> StartProcessInstanceAsync(
        StartProcessInstanceRequest request,
        CallContext                 context = default
    );

    /// <summary>Completes the current activity and auto-advances the process instance.</summary>
    [Operation]
    ValueTask<ProcessInstance> CompleteActivityAsync(CompleteActivityRequest request, CallContext context = default);

    /// <summary>Correlates a named message to a specific process instance.</summary>
    [Operation]
    ValueTask<ProcessInstance> CorrelateMessageAsync(CorrelateMessageRequest request, CallContext context = default);

    /// <summary>Throws (broadcasts) a signal to all waiting process instances.</summary>
    [Operation]
    ValueTask ThrowSignalAsync(ThrowSignalRequest request, CallContext context = default);

    /// <summary>Terminates a process instance immediately.</summary>
    [Operation]
    ValueTask<ProcessInstance> TerminateProcessInstanceAsync(
        TerminateProcessInstanceRequest request,
        CallContext                     context = default
    );

    /// <summary>Gets a single process instance by its canonical name.</summary>
    [Operation]
    ValueTask<SchemataProcess?> GetProcessInstanceAsync(
        GetProcessInstanceRequest request,
        CallContext               context = default
    );

    /// <summary>Lists all process instances.</summary>
    [Operation]
    ValueTask<ListProcessInstancesResponse> ListProcessInstancesAsync(
        ListProcessInstancesRequest request,
        CallContext                 context = default
    );

    /// <summary>Gets a single transition record by its canonical name.</summary>
    [Operation]
    ValueTask<SchemataProcessTransition?> GetProcessInstanceTransitionAsync(
        GetProcessInstanceTransitionRequest request,
        CallContext                         context = default
    );

    /// <summary>Lists all transition records for a process instance.</summary>
    [Operation]
    ValueTask<ListProcessInstanceTransitionsResponse> ListProcessInstanceTransitionsAsync(
        ListProcessInstanceTransitionsRequest request,
        CallContext                           context = default
    );

    /// <summary>Lists all registered process definitions.</summary>
    [Operation]
    ValueTask<ListProcessDefinitionsResponse> ListProcessDefinitionsAsync(
        ListProcessDefinitionsRequest request,
        CallContext                   context = default
    );
}
