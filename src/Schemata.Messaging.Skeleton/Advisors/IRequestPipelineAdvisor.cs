using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;

namespace Schemata.Messaging.Skeleton.Advisors;

/// <summary>
///     Wraps the dispatch of a <typeparamref name="TRequest" />: the segment before
///     <c>await next(ct)</c> runs ahead of the handler, the segment after it runs on the produced
///     <typeparamref name="TResponse" /> and may rewrite it.
/// </summary>
/// <remarks>
///     The dispatcher composes registered advisors into a chain in ascending
///     <see cref="IAdvisor.Order" />, with the handler at the tail, and shares one ambient
///     <see cref="AdviceContext" /> across the whole chain and the handler. An advisor that does not
///     call <c>next</c> short-circuits: it returns a response it constructs, or throws to abort. The
///     request envelope is not passed to <c>next</c>, so an advisor cannot substitute the request
///     subject downstream.
/// </remarks>
/// <typeparam name="TRequest">The request type this advisor wraps.</typeparam>
/// <typeparam name="TResponse">The response the request produces.</typeparam>
public interface IRequestPipelineAdvisor<in TRequest, TResponse> : IAdvisor
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    ///     Wraps the continuation for one dispatch of <paramref name="request" />.
    /// </summary>
    /// <param name="ctx">The ambient <see cref="AdviceContext" /> shared with the handler.</param>
    /// <param name="request">The request being dispatched.</param>
    /// <param name="next">The remainder of the pipeline; omit the call to short-circuit.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The response, either from <paramref name="next" /> or constructed by the advisor.</returns>
    Task<TResponse> AdviseAsync(
        AdviceContext                      ctx,
        TRequest                           request,
        RequestHandlerContinuation<TResponse> next,
        CancellationToken                  ct);
}
