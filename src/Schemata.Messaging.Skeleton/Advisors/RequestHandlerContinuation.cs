using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Messaging.Skeleton.Advisors;

/// <summary>
///     The remainder of a request pipeline: the next
///     <see cref="IRequestPipelineAdvisor{TRequest,TResponse}" /> or, at the tail, the request's
///     single handler. An advisor calls it to proceed and awaits the produced
///     <typeparamref name="TResponse" />, or omits the call to short-circuit.
/// </summary>
/// <typeparam name="TResponse">The response flowing back out of the pipeline.</typeparam>
/// <param name="ct">A cancellation token.</param>
public delegate Task<TResponse> RequestHandlerContinuation<TResponse>(CancellationToken ct);
