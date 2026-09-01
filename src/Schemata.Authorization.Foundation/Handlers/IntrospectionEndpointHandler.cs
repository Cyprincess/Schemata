using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Foundation.Queries;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Handlers;

internal sealed class IntrospectionEndpointHandler(IntrospectionEndpoint endpoint)
    : IRequestHandler<IntrospectionEndpointQuery, IntrospectionResponse>
{
    public Task<IntrospectionResponse> HandleAsync(IntrospectionEndpointQuery request, CancellationToken ct = default)
        => endpoint.HandleAsync(request.Request, request.Headers, ct);
}