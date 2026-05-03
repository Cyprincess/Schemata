using Schemata.Abstractions.Advisors;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>
///     Advisor invoked after the query is executed, allowing post-processing of the result.
///     The result is available via <see cref="QueryContext{TEntity,TResult,T}.Result" />.
/// </summary>
/// <typeparam name="TEntity">The root entity type that was queried.</typeparam>
/// <typeparam name="TResult">The projected result type of the query.</typeparam>
/// <typeparam name="T">
///     The scalar or aggregate return type (e.g., <see cref="bool" />, <see cref="int" />).
/// </typeparam>
public interface IRepositoryResultAdvisor<TEntity, TResult, T> : IAdvisor<QueryContext<TEntity, TResult, T>>
    where TEntity : class;
