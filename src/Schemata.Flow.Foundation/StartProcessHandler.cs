using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Foundation;

/// <summary>Resource method handler that starts process instances from registered definitions.</summary>
public sealed class StartProcessHandler(FlowRunner runner)
    : IResourceMethodHandler<SchemataProcess, StartProcessInstanceRequest, SchemataProcess>
{
    #region IResourceMethodHandler<SchemataProcess,StartProcessInstanceRequest,SchemataProcess> Members

    public ValueTask<SchemataProcess> InvokeAsync(
        string?                     name,
        StartProcessInstanceRequest request,
        SchemataProcess?            entity,
        ClaimsPrincipal?            principal,
        CancellationToken           ct
    ) {
        var options = new StartProcessOptions {
            DisplayName = request.DisplayName,
            Description = request.Description,
        };
        return runner.StartAsync(request.DefinitionName, request.Source, options, principal, ct);
    }

    #endregion
}
