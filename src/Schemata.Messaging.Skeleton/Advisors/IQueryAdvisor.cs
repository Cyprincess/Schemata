using Schemata.Abstractions.Advisors;

namespace Schemata.Messaging.Skeleton.Advisors;

/// <summary>
///     Advisor running around the dispatch of a <typeparamref name="TQuery" />, before its handler is
///     resolved.
/// </summary>
/// <remarks>
///     Standard pipeline semantics: advisors run in ascending <see cref="IAdvisor.Order" /> and the
///     chain stops at the first non-<see cref="AdviseResult.Continue" /> result.
///     <see cref="AdviseResult.Block" /> aborts the dispatch and the handler never runs;
///     <see cref="AdviseResult.Handle" /> means the advisor produced the result itself — a cache hit
///     or a read-replica answer — which it supplies by calling <see cref="AdviceContext.Set{T}" />
///     with the query's declared result type.
/// </remarks>
/// <remarks>
///     Single type parameter, mirroring <see cref="ICommandAdvisor{TCommand}" />. A second
///     <c>TResult</c> parameter cannot be carried here: a dispatcher implements
///     <see cref="Schemata.Messaging.Skeleton.IRequestDispatcher.SendAsync{TRequest,TResponse}" />
///     under <c>TRequest : IRequest&lt;TResponse&gt;</c>, which does not satisfy a
///     <c>TQuery : IQuery&lt;TResult&gt;</c> constraint, so the advisor would be unreferenceable
///     from the very place that runs it. The query type already determines its result through
///     <see cref="IQuery{TResult}" />, so nothing is lost.
/// </remarks>
/// <typeparam name="TQuery">The query type this advisor observes.</typeparam>
public interface IQueryAdvisor<in TQuery> : IAdvisor<TQuery>;
