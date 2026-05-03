using Schemata.Abstractions.Advisors;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>
///     Advisor invoked after the query is built but before it is executed against the data store.
///     Returning <see cref="AdviseResult.Handle" /> short-circuits execution and returns
///     <see cref="QueryContext{TEntity,TResult,T}.Result" />; e.g., to serve a cached value
///     without hitting the database.
///     Returning <see cref="AdviseResult.Block" /> returns default.
/// </summary>
/// <typeparam name="TEntity">The root entity type being queried.</typeparam>
/// <typeparam name="TResult">The projected result type of the query.</typeparam>
/// <typeparam name="T">
///     The scalar or aggregate return type (e.g., <see cref="bool" />, <see cref="int" />).
/// </typeparam>
public interface IRepositoryQueryAdvisor<TEntity, TResult, T> : IAdvisor<QueryContext<TEntity, TResult, T>>
    where TEntity : class;
