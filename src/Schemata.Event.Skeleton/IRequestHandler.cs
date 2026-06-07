using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Event.Skeleton;

/// <summary>Handles <typeparamref name="TRequest" /> and returns a <typeparamref name="TResponse" />.</summary>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>Processes <paramref name="request" /> and returns the response.</summary>
    Task<TResponse> HandleAsync(TRequest request, CancellationToken ct = default);
}
