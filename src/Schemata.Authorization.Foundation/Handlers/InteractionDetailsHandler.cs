using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Foundation.Queries;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Handlers;

internal sealed class InteractionDetailsHandler(InteractionEndpoint endpoint)
    : IRequestHandler<InteractionDetailsQuery, AuthorizationResult>
{
    public Task<AuthorizationResult> HandleAsync(InteractionDetailsQuery request, CancellationToken ct = default)
        => endpoint.GetDetailsAsync(request.Request, request.Issuer, ct);
}