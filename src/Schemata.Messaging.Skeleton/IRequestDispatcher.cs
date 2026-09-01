using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Messaging.Skeleton;

/// <summary>
///     Routes a request to its single handler. Implementations may dispatch in-process or bridge to
///     an out-of-process transport; the contract does not change with the transport.
/// </summary>
public interface IRequestDispatcher
{
    /// <summary>Dispatches <paramref name="request" /> to its single handler and returns the response.</summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="request">The request instance.</param>
    /// <param name="ct">A cancellation token.</param>
    Task<TResponse> SendAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
        where TRequest : IRequest<TResponse>;
}
