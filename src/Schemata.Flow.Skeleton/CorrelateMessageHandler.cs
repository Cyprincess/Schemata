using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Skeleton.Utilities;

namespace Schemata.Flow.Skeleton;

/// <summary>Resource method handler that correlates a message to a process instance.</summary>
public sealed class CorrelateMessageHandler(
    IProcessRuntime  runtime,
    IProcessRegistry registry
) : IResourceMethodHandler<SchemataProcess, CorrelateMessageRequest, ProcessInstance>
{
    #region IResourceMethodHandler<SchemataProcess,CorrelateMessageRequest,ProcessInstance> Members

    public async ValueTask<ProcessInstance> InvokeAsync(
        string?                 name,
        CorrelateMessageRequest request,
        SchemataProcess?        entity,
        ClaimsPrincipal?        principal,
        CancellationToken       ct
    ) {
        ArgumentNullException.ThrowIfNull(entity);
        FlowProcessAuthorization.EnsureProcessAccess(registry, entity, principal);

        var payload = request.Payload is not null ? VariableSerializer.Deserialize(request.Payload) : null;
        var instance = await runtime.CorrelateMessageAsync(name!, request.MessageName, payload, principal, ct);
        instance.CanonicalName = name;
        instance.Name = entity.Name;
        return instance;
    }

    #endregion
}
