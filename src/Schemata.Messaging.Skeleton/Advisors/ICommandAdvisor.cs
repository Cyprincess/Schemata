using Schemata.Abstractions.Advisors;

namespace Schemata.Messaging.Skeleton.Advisors;

/// <summary>
///     Advisor running around the dispatch of a <typeparamref name="TCommand" />, before its handler
///     is resolved.
/// </summary>
/// <remarks>
///     Standard pipeline semantics: advisors run in ascending <see cref="IAdvisor.Order" /> and the
///     chain stops at the first non-<see cref="AdviseResult.Continue" /> result.
///     <see cref="AdviseResult.Block" /> aborts the dispatch and the handler never runs;
///     <see cref="AdviseResult.Handle" /> means the advisor produced the outcome itself, which it
///     supplies by calling <see cref="AdviceContext.Set{T}" /> with the result type.
/// </remarks>
/// <typeparam name="TCommand">The command type this advisor observes.</typeparam>
public interface ICommandAdvisor<in TCommand> : IAdvisor<TCommand>;
