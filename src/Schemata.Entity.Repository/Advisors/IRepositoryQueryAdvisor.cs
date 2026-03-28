using Schemata.Abstractions.Advisors;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>
///     Advisor invoked after the query is built but before it is executed against the data store.
/// </summary>
/// <typeparam name="TEntity">The root entity type being queried.</typeparam>
/// <typeparam name="TResult">The projected result type of the query.</typeparam>
/// <typeparam name="T">
///     The scalar or aggregate return type (e.g., the entity itself, <see cref="bool" />,
///     <see cref="int" />).
/// </typeparam>
/// <remarks>
///     Returning <see cref="AdviseResult.Handle" /> short-circuits execution and returns
///     <see cref="QueryContext{TEntity,TResult,T}.Result" />.
///     Returning <see cref="AdviseResult.Block" /> returns default.
///     Returning <see cref="AdviseResult.Continue" /> allows the query to execute normally.
///     This is used by the cache advisor to return cached results without hitting the database.
/// </remarks>
public interface IRepositoryQueryAdvisor<TEntity, TResult, T> : IAdvisor<QueryContext<TEntity, TResult, T>>
    where TEntity : class;
