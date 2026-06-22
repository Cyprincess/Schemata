using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Foundation;

/// <summary>Resource method handler that terminates process instances.</summary>
public sealed class TerminateProcessHandler(
    IProcessRuntime  runtime,
    IProcessRegistry registry
) : IResourceMethodHandler<SchemataProcess, EmptyResourceRequest, ProcessInstance>
{
    #region IResourceMethodHandler<SchemataProcess,EmptyResourceRequest,ProcessInstance> Members

    public async ValueTask<ProcessInstance> InvokeAsync(
        string?              name,
        EmptyResourceRequest request,
        SchemataProcess?     entity,
        ClaimsPrincipal?     principal,
        CancellationToken    ct
    ) {
        ArgumentNullException.ThrowIfNull(entity);
        FlowProcessAuthorization.EnsureProcessAccess(registry, entity, principal);

        var instance = await runtime.TerminateProcessInstanceAsync(name!, principal, ct);
        instance.CanonicalName = name;
        instance.Name = entity.Name;
        return instance;
    }

    #endregion
}
