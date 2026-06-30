using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Foundation;

/// <summary>Resource method handler that completes the current activity for a process instance.</summary>
public sealed class CompleteActivityHandler(FlowRunner runner)
    : IResourceMethodHandler<SchemataProcess, CompleteActivityRequest, ProcessSnapshot>
{
    #region IResourceMethodHandler<SchemataProcess,CompleteActivityRequest,ProcessSnapshot> Members

    public ValueTask<ProcessSnapshot> InvokeAsync(
        string?                 name,
        CompleteActivityRequest request,
        SchemataProcess?        entity,
        ClaimsPrincipal?        principal,
        CancellationToken       ct
    ) {
        ArgumentNullException.ThrowIfNull(entity);
        return runner.CompleteAsync(entity, request.Token, principal, ct);
    }

    #endregion
}
