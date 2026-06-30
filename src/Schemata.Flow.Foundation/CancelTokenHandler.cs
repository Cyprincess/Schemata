using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Foundation;

/// <summary>Resource method handler that cancels a single execution token.</summary>
public sealed class CancelTokenHandler(FlowRunner runner)
    : IResourceMethodHandler<SchemataProcessToken, EmptyResourceRequest, ProcessSnapshot>
{
    #region IResourceMethodHandler<SchemataProcessToken,EmptyResourceRequest,ProcessSnapshot> Members

    public ValueTask<ProcessSnapshot> InvokeAsync(
        string?               name,
        EmptyResourceRequest  request,
        SchemataProcessToken? entity,
        ClaimsPrincipal?      principal,
        CancellationToken     ct
    ) {
        ArgumentNullException.ThrowIfNull(entity);
        return runner.CancelTokenAsync(entity, principal, ct);
    }

    #endregion
}
