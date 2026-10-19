using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;

namespace Schemata.Messaging.Skeleton;

/// <summary>Handles <typeparamref name="TRequest" /> and returns a <typeparamref name="TResponse" />.</summary>
/// <typeparam name="TRequest">The request type this handler answers.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    ///     Processes <paramref name="request" /> and returns the response.
    /// </summary>
    /// <param name="request">The request to process.</param>
    /// <param name="context">The dispatch's advisor context, shared with the request pipeline.</param>
    /// <param name="ct">A cancellation token.</param>
    Task<TResponse> HandleAsync(TRequest request, AdviceContext context, CancellationToken ct = default);
}
