using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Foundation.Queries;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Handlers;

internal sealed class RegisterEndpointHandler(RegisterEndpoint endpoint)
    : IRequestHandler<RegisterEndpointQuery, RegistrationResponse>
{
    public Task<RegistrationResponse> HandleAsync(RegisterEndpointQuery request, CancellationToken ct = default)
        => endpoint.HandleAsync(request.Request, request.BearerToken, ct);
}
