using Schemata.Abstractions.Advisors;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>
///     Advisor invoked after the query is executed, allowing post-processing of the result.
/// </summary>
/// <typeparam name="TEntity">The root entity type that was queried.</typeparam>
/// <typeparam name="TResult">The projected result type of the query.</typeparam>
/// <typeparam name="T">
///     The scalar or aggregate return type (e.g., the entity itself, <see cref="bool" />,
///     <see cref="int" />).
/// </typeparam>
/// <remarks>
///     The result is available via <see cref="QueryContext{TEntity,TResult,T}.Result" />.
///     Returning <see cref="AdviseResult.Block" /> discards the result and returns default.
///     Returning <see cref="AdviseResult.Continue" /> or <see cref="AdviseResult.Handle" /> returns the result.
///     This is used by the cache advisor to store results in the cache after execution.
/// </remarks>
public interface IRepositoryResultAdvisor<TEntity, TResult, T> : IAdvisor<QueryContext<TEntity, TResult, T>>
    where TEntity : class;
