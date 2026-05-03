using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Entity.Repository;

/// <summary>
///     Generic repository interface providing strongly-typed CRUD operations with an
///     advisor pipeline. All query methods route through build-query advisors
///     (see <see cref="IRepositoryBuildQueryAdvisor{TEntity}" />); mutation methods
///     route through add, update, or remove advisors respectively.
/// </summary>
/// <typeparam name="TEntity">The entity type managed by this repository.</typeparam>
public interface IRepository<TEntity>
    where TEntity : class
{
    /// <summary>
    ///     Carries the advice context that gates advisor execution and holds suppression
    ///     flags for this repository instance.
    /// </summary>
    AdviceContext AdviceContext { get; }

    /// <summary>
    ///     Returns all entities as an async enumerable, bypassing the build-query advisor
    ///     pipeline. Prefer <see cref="ListAsync{TResult}" /> for filtered queries that
    ///     respect soft-delete and other global filters, per
    ///     <seealso href="https://google.aip.dev/160">AIP-160: Filtering</seealso>.
    /// </summary>
    IAsyncEnumerable<TEntity> AsAsyncEnumerable();

    /// <summary>
    ///     Returns all entities as a queryable, bypassing the build-query advisor pipeline.
    ///     Prefer <see cref="ListAsync{TResult}" /> for filtered queries that respect
    ///     soft-delete and other global filters, per
    ///     <seealso href="https://google.aip.dev/160">AIP-160: Filtering</seealso>.
    /// </summary>
    IQueryable<TEntity> AsQueryable();

    /// <summary>
    ///     Enumerates entities through the build-query advisor pipeline
    ///     (see <see cref="IRepositoryBuildQueryAdvisor{TEntity}" />), projected by the
    ///     predicate.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="predicate">
    ///     An optional query transformation. When <see langword="null" />, this is equivalent
    ///     to calling <see cref="Queryable.OfType{TResult}" /> on the advisor-processed queryable.
    /// </param>
    /// <param name="ct">A cancellation token.</param>
    IAsyncEnumerable<TResult> ListAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    );

    /// <summary>
    ///     Searches entities through the build-query advisor pipeline and provider-specific
    ///     full-text search, projected by the predicate.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="predicate">An optional query transformation.</param>
    /// <param name="ct">A cancellation token.</param>
    IAsyncEnumerable<TResult> SearchAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    );

    /// <summary>
    ///     Retrieves an entity by matching its key property values against the provided
    ///     entity instance.
    /// </summary>
    /// <param name="entity">
    ///     An entity whose key properties are used to build the lookup. Only key values
    ///     are considered; other fields are ignored.
    /// </param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The matching entity or <see langword="null" />.</returns>
    ValueTask<TEntity?> GetAsync(TEntity entity, CancellationToken ct = default);

    /// <summary>
    ///     Retrieves an entity by its keys and projects it to <typeparamref name="TResult" />.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="entity">An entity whose key properties are used to build the lookup.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask<TResult?> GetAsync<TResult>(TEntity entity, CancellationToken ct = default);

    /// <summary>
    ///     Finds an entity by its primary key values.
    /// </summary>
    /// <param name="keys">The primary key values in property declaration order.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask<TEntity?> FindAsync(object[] keys, CancellationToken ct = default);

    /// <summary>
    ///     Finds an entity by its primary key values and projects it to
    ///     <typeparamref name="TResult" />.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="keys">The primary key values in property declaration order.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask<TResult?> FindAsync<TResult>(object[] keys, CancellationToken ct = default);

    /// <summary>
    ///     Returns the first matching result or <see langword="null" />, after build-query
    ///     advisors are applied.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="predicate">An optional query transformation.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask<TResult?> FirstOrDefaultAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    );

    /// <summary>
    ///     Returns the single matching result or <see langword="null" />, after build-query
    ///     advisors are applied.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="predicate">An optional query transformation.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask<TResult?> SingleOrDefaultAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    );

    /// <summary>
    ///     Returns <see langword="true" /> if any entity matches the predicate, after
    ///     build-query advisors are applied.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="predicate">An optional query transformation.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask<bool> AnyAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    );

    /// <summary>
    ///     Returns the count of entities matching the predicate, after build-query advisors
    ///     are applied.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="predicate">An optional query transformation.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask<int> CountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    );

    /// <summary>
    ///     Returns the long count of entities matching the predicate, after build-query
    ///     advisors are applied.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="predicate">An optional query transformation.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask<long> LongCountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    );

    /// <summary>
    ///     Drives the entity through the add advisor pipeline
    ///     (see <see cref="IRepositoryAddAdvisor{TEntity}" />) before persistence.
    /// </summary>
    /// <param name="entity">The entity to persist.</param>
    /// <param name="ct">A cancellation token.</param>
    Task AddAsync(TEntity entity, CancellationToken ct = default);

    /// <summary>
    ///     Drives each entity through the add advisor pipeline before persistence.
    /// </summary>
    /// <param name="entities">The entities to persist.</param>
    /// <param name="ct">A cancellation token.</param>
    Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default);

    /// <summary>
    ///     Drives the entity through the update advisor pipeline
    ///     (see <see cref="IRepositoryUpdateAdvisor{TEntity}" />) before persistence.
    /// </summary>
    /// <param name="entity">The entity to persist with updated values.</param>
    /// <param name="ct">A cancellation token.</param>
    Task UpdateAsync(TEntity entity, CancellationToken ct = default);

    /// <summary>
    ///     Drives the entity through the remove advisor pipeline
    ///     (see <see cref="IRepositoryRemoveAdvisor{TEntity}" />). When soft-delete
    ///     is active and not suppressed, <see cref="AdviceRemoveSoftDelete{TEntity}" />
    ///     intercepts the deletion and converts it to a soft-delete update, per
    ///     <seealso href="https://google.aip.dev/164">AIP-164: Soft delete</seealso>.
    /// </summary>
    /// <param name="entity">The entity to remove.</param>
    /// <param name="ct">A cancellation token.</param>
    Task RemoveAsync(TEntity entity, CancellationToken ct = default);

    /// <summary>
    ///     Drives each entity through the remove advisor pipeline.
    /// </summary>
    /// <param name="entities">The entities to remove.</param>
    /// <param name="ct">A cancellation token.</param>
    Task RemoveRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default);

    /// <summary>
    ///     Persists all pending changes to the underlying data store.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The number of rows affected.</returns>
    ValueTask<int> CommitAsync(CancellationToken ct = default);

    /// <summary>
    ///     Detaches an entity from the change tracker so subsequent mutations are not persisted.
    /// </summary>
    /// <param name="entity">The entity to detach.</param>
    void Detach(TEntity entity);

    /// <summary>
    ///     Creates a new repository instance with a fresh <see cref="AdviceContext" />,
    ///     isolating any subsequent <c>Suppress*</c> calls.
    /// </summary>
    /// <returns>A new <see cref="IRepository{TEntity}" /> instance.</returns>
    IRepository<TEntity> Once();

    IRepository<TEntity> SuppressAddValidation();

    IRepository<TEntity> SuppressUpdateValidation();

    IRepository<TEntity> SuppressConcurrency();

    IRepository<TEntity> SuppressQuerySoftDelete();

    IRepository<TEntity> SuppressSoftDelete();

    IRepository<TEntity> SuppressTimestamp();
}
