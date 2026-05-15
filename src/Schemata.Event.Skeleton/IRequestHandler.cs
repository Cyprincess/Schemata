using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Event.Skeleton;

public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, CancellationToken ct = default);
}
