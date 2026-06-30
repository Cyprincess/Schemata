using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Foundation;

/// <summary>Resource method handler that correlates a message to a process instance.</summary>
public sealed class CorrelateMessageHandler(FlowRunner runner)
    : IResourceMethodHandler<SchemataProcess, CorrelateMessageRequest, ProcessSnapshot>
{
    #region IResourceMethodHandler<SchemataProcess,CorrelateMessageRequest,ProcessSnapshot> Members

    public ValueTask<ProcessSnapshot> InvokeAsync(
        string?                 name,
        CorrelateMessageRequest request,
        SchemataProcess?        entity,
        ClaimsPrincipal?        principal,
        CancellationToken       ct
    ) {
        ArgumentNullException.ThrowIfNull(entity);
        return runner.CorrelateAsync(entity, request.MessageName, request.Payload, request.Token, principal, ct);
    }

    #endregion
}
