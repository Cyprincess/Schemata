using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Skeleton.Utilities;

namespace Schemata.Flow.Skeleton;

public sealed class ThrowSignalHandler(
    IProcessRuntime  runtime,
    IProcessRegistry registry
) : IResourceMethodHandler<SchemataProcess, ThrowSignalRequest, EmptyResourceRequest>
{
    #region IResourceMethodHandler<SchemataProcess,ThrowSignalRequest,EmptyResourceRequest> Members

    public async ValueTask<EmptyResourceRequest> InvokeAsync(
        string?             name,
        ThrowSignalRequest  request,
        SchemataProcess?    entity,
        ClaimsPrincipal?    principal,
        CancellationToken   ct
    ) {
        FlowProcessAuthorization.EnsureSignalAccess(registry, request.SignalName, principal);

        var payload = request.Payload is not null ? VariableSerializer.Deserialize(request.Payload) : null;
        await runtime.ThrowSignalAsync(request.SignalName, payload, principal, ct);
        return new() { CanonicalName = "processes" };
    }

    #endregion
}
