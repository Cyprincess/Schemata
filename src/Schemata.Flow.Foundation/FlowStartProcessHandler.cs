using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;
using StartProcessCommand = Schemata.Flow.Foundation.Commands.StartProcessRequest;

namespace Schemata.Flow.Foundation;

/// <summary>
///     Handles process-instance start requests dispatched through the resource-method pipeline.
/// </summary>
public sealed class FlowStartProcessHandler(
    IRequestDispatcher dispatcher,
    FlowSourceLoader   sources)
    : IRequestHandler<StartProcessInstanceRequest, SchemataProcess>
{
    public async Task<SchemataProcess> HandleAsync(
        StartProcessInstanceRequest request,
        CancellationToken ct = default)
    {
        var principal = request.Principal;

        var options = new StartProcessOptions {
            DisplayName    = request.DisplayName,
            Description    = request.Description,
            IdempotencyKey = request.IdempotencyKey,
        };

        if (string.IsNullOrWhiteSpace(request.Source)) {
            return await dispatcher.SendAsync<StartProcessCommand, SchemataProcess>(new(
                request.DefinitionName,
                Source: null,
                SourceType: null,
                SourceCanonicalName: null,
                options,
                principal), ct);
        }

        var command = await sources.CreateRequestAsync(
            request.DefinitionName, request.Source, options, principal, ct);
        return await dispatcher.SendAsync<StartProcessCommand, SchemataProcess>(command, ct);
    }
}