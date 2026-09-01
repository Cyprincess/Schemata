using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Messaging.Skeleton;

/// <summary>Handles <typeparamref name="TRequest" /> and returns a <typeparamref name="TResponse" />.</summary>
/// <typeparam name="TRequest">The request type this handler answers.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>Processes <paramref name="request" /> and returns the response.</summary>
    Task<TResponse> HandleAsync(TRequest request, CancellationToken ct = default);
}
