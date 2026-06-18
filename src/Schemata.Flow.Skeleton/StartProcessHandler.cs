using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Skeleton.Utilities;

namespace Schemata.Flow.Skeleton;

public sealed class StartProcessHandler(
    IProcessRuntime  runtime,
    IProcessRegistry registry
) : IResourceMethodHandler<SchemataProcess, StartProcessInstanceRequest, SchemataProcess>
{
    #region IResourceMethodHandler<SchemataProcess,StartProcessInstanceRequest,SchemataProcess> Members

    public async ValueTask<SchemataProcess> InvokeAsync(
        string?                     name,
        StartProcessInstanceRequest request,
        SchemataProcess?            entity,
        ClaimsPrincipal?            principal,
        CancellationToken           ct
    ) {
        FlowProcessAuthorization.EnsureDefinitionAccess(registry, request.DefinitionName, principal);

        var variables = request.Variables is not null ? VariableSerializer.Deserialize(request.Variables) : null;

        // Display name and description flow into the start transaction so the instance is never
        // persisted without the metadata the caller supplied.
        return await runtime.StartProcessInstanceAsync(
            request.DefinitionName,
            variables,
            principal,
            request.DisplayName,
            request.Description,
            null,
            ct);
    }

    #endregion
}
