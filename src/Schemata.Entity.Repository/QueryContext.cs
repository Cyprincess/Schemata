using System.Linq;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Entity.Repository;

/// <summary>
///     Carries the repository, constructed queryable, and result through the query and result
///     advisor pipelines (<see cref="IRepositoryQueryAdvisor{TEntity,TResult,T}" /> and
///     <see cref="IRepositoryResultAdvisor{TEntity, TResult, T}" />).
/// </summary>
/// <typeparam name="TEntity">The root entity type being queried.</typeparam>
/// <typeparam name="TResult">The projected result type of the query.</typeparam>
/// <typeparam name="T">
///     The scalar or aggregate return type (e.g., <see cref="bool" />, <see cref="int" />).
/// </typeparam>
public class QueryContext<TEntity, TResult, T>
    where TEntity : class
{
    /// <summary>
    ///     Initializes a context for executing the given queryable.
    /// </summary>
    /// <param name="repository">The repository that initiated this query.</param>
    /// <param name="query">
    ///     The queryable after all build-query advisors and the user predicate have been applied.
    /// </param>
    public QueryContext(IRepository<TEntity> repository, IQueryable<TResult> query) {
        Repository = repository;
        Query      = query;
    }

    /// <summary>
    ///     The repository that initiated this query.
    /// </summary>
    public IRepository<TEntity> Repository { get; private set; }

    /// <summary>
    ///     The queryable ready for execution.
    /// </summary>
    public IQueryable<TResult> Query { get; private set; }

    /// <summary>
    ///     The result produced by executing <see cref="Query" />, or set by a query advisor that
    ///     short-circuits execution. Initialized to <see langword="default" />.
    /// </summary>
    public T? Result { get; set; }
}
