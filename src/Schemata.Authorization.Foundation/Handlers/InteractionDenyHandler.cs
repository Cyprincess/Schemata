using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Handlers;

internal sealed class InteractionDenyHandler(InteractionEndpoint endpoint)
    : IRequestHandler<InteractionDenyRequest, Unit>
{
    public async Task<Unit> HandleAsync(InteractionDenyRequest request, CancellationToken ct = default)
    {
        await endpoint.DenyAsync(request.Request, ct);
        return Unit.Value;
    }
}