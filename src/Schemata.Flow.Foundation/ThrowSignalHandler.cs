using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Foundation;

/// <summary>Resource method handler that broadcasts BPMN signals to waiting process instances.</summary>
public sealed class ThrowSignalHandler(FlowRunner runner)
    : IResourceMethodHandler<SchemataProcess, ThrowSignalRequest, EmptyResourceResponse>
{
    #region IResourceMethodHandler<SchemataProcess,ThrowSignalRequest,EmptyResourceResponse> Members

    public async ValueTask<EmptyResourceResponse> InvokeAsync(
        string?            name,
        ThrowSignalRequest request,
        SchemataProcess?   entity,
        ClaimsPrincipal?   principal,
        CancellationToken  ct
    ) {
        await runner.ThrowSignalAsync(request.SignalName, request.Payload, request.Token, principal, ct);
        return new();
    }

    #endregion
}
