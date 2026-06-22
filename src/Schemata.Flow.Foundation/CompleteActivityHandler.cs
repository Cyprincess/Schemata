using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Skeleton.Utilities;

namespace Schemata.Flow.Foundation;

/// <summary>Resource method handler that completes the current activity for a process instance.</summary>
public sealed class CompleteActivityHandler(
    IProcessRuntime  runtime,
    IProcessRegistry registry
) : IResourceMethodHandler<SchemataProcess, CompleteActivityRequest, ProcessInstance>
{
    #region IResourceMethodHandler<SchemataProcess,CompleteActivityRequest,ProcessInstance> Members

    public async ValueTask<ProcessInstance> InvokeAsync(
        string?                 name,
        CompleteActivityRequest request,
        SchemataProcess?        entity,
        ClaimsPrincipal?        principal,
        CancellationToken       ct
    ) {
        ArgumentNullException.ThrowIfNull(entity);
        FlowProcessAuthorization.EnsureProcessAccess(registry, entity, principal);

        var variables = request.Variables is not null ? VariableSerializer.Deserialize(request.Variables) : null;
        var instance = await runtime.CompleteActivityAsync(name!, variables, principal, ct);
        instance.CanonicalName = name;
        instance.Name = entity.Name;
        return instance;
    }

    #endregion
}
