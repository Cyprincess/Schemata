using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Handlers;

internal sealed class RevokeEndpointHandler(RevocationEndpoint endpoint)
    : IRequestHandler<RevokeEndpointRequest, Unit>
{
    public async Task<Unit> HandleAsync(RevokeEndpointRequest request, CancellationToken ct = default)
    {
        await endpoint.HandleAsync(request.Request, request.Headers, ct);
        return Unit.Value;
    }
}