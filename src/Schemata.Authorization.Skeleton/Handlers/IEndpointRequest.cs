using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Authorization.Skeleton.Handlers;

/// <summary>
///     A request record that knows how to execute against its endpoint abstraction. Carrying
///     the invocation on the record lets a single generic dispatch handler serve every
///     endpoint without per-endpoint forwarding wrappers.
/// </summary>
/// <typeparam name="TEndpoint">The endpoint abstraction the request executes against.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IEndpointRequest<TEndpoint, TResponse>
{
    /// <summary>Executes the request against the endpoint.</summary>
    Task<TResponse> ExecuteAsync(TEndpoint endpoint, CancellationToken ct);
}
