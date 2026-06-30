using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Foundation;

/// <summary>Resource method handler that terminates process instances.</summary>
public sealed class TerminateProcessHandler(FlowRunner runner)
    : IResourceMethodHandler<SchemataProcess, EmptyResourceRequest, ProcessSnapshot>
{
    #region IResourceMethodHandler<SchemataProcess,EmptyResourceRequest,ProcessSnapshot> Members

    public ValueTask<ProcessSnapshot> InvokeAsync(
        string?              name,
        EmptyResourceRequest request,
        SchemataProcess?     entity,
        ClaimsPrincipal?     principal,
        CancellationToken    ct
    ) {
        ArgumentNullException.ThrowIfNull(entity);
        return runner.TerminateAsync(entity, principal, ct);
    }

    #endregion
}
