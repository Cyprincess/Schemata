using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Handlers;

/// <summary>
///     Single generic dispatch handler for every Connect endpoint: resolves the endpoint
///     method shape. Replaces the per-endpoint forwarding wrappers.
/// </summary>
/// <typeparam name="TRequest">The request record type.</typeparam>
/// <typeparam name="TEndpoint">The endpoint abstraction.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
internal sealed class EndpointDispatchHandler<TRequest, TEndpoint, TResponse>(TEndpoint endpoint)
    : IRequestHandler<TRequest, TResponse>
    where TRequest : IEndpointRequest<TEndpoint, TResponse>, IRequest<TResponse>
{
    public Task<TResponse> HandleAsync(TRequest request, CancellationToken ct = default) {
        return request.ExecuteAsync(endpoint, ct);
    }
}
