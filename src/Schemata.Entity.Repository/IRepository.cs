using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Entity.Repository;

/// <summary>
///     Non-generic repository contract carrying the entity-agnostic surface (advisor
///     context, unit-of-work enlistment, commit, and suppression scopes). Framework code
///     that coordinates repositories without knowing the concrete entity type takes a
///     dependency on this interface; entity-specific work uses
///     <see cref="IRepository{TEntity}" />.
/// </summary>
public interface IRepository : IAsyncDisposable, IDisposable
{
    /// <summary>
    ///     Carries the advice context that gates advisor execution and holds suppression
    ///     flags for this repository instance.
    /// </summary>
    AdviceContext AdviceContext { get; }

    /// <summary>
    ///     Begins a unit of work bound to this repository's data context. The first
    ///     <see cref="Join" /> on the returned unit of work opens the underlying connection
    ///     and transaction; subsequent enlistments share that context.
    /// </summary>
    /// <returns>A new unit of work that callers must commit, roll back, or dispose.</returns>
    IUnitOfWork Begin();

    /// <summary>
    ///     Enlists this repository in an existing unit of work. The repository then
    ///     writes through the unit-of-work's context.
    /// </summary>
    /// <param name="uow">The unit of work to join.</param>
    void Join(IUnitOfWork uow);

    /// <summary>
    ///     Persists all pending changes to the underlying data store.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    Task CommitAsync(CancellationToken ct = default);

    /// <summary>
    ///     Suppresses the add-side validation advisor for the duration of the returned
    ///     scope. Disposing the returned handle restores the previous state.
    /// </summary>
    IDisposable SuppressAddValidation();

    /// <summary>
    ///     Suppresses the update-side validation advisor for the duration of the returned
    ///     scope. Disposing the returned handle restores the previous state.
    /// </summary>
    IDisposable SuppressUpdateValidation();

    /// <summary>
    ///     Suppresses the soft-delete filter on build-query for the duration of the
    ///     returned scope. Disposing the returned handle restores the previous state.
    /// </summary>
    IDisposable SuppressQuerySoftDelete();

    /// <summary>
    ///     Suppresses the soft-delete behavior on add and remove for the duration of the
    ///     returned scope. Disposing the returned handle restores the previous state.
    /// </summary>
    IDisposable SuppressSoftDelete();

    /// <summary>
    ///     Suppresses the timestamp advisors (add and update) for the duration of the
    ///     returned scope. Disposing the returned handle restores the previous state.
    /// </summary>
    IDisposable SuppressTimestamp();
}

/// <summary>
///     Generic repository interface providing strongly-typed CRUD operations with an
///     advisor pipeline. All query methods route through build-query advisors
///     (see <see cref="IRepositoryBuildQueryAdvisor{TEntity}" />); mutation methods
///     route through add, update, or remove advisors respectively.
/// </summary>
/// <remarks>
///     Provider-specific behavior for pre-commit reads: EF Core does not surface
///     uncommitted writes through LINQ queries because mutations are buffered in the
///     change tracker until <see cref="IRepository.CommitAsync" /> flushes them. LinqToDB
///     does surface uncommitted writes within the active transaction
///     (read-your-own-writes). Both providers behave identically after
///     <see cref="IRepository.CommitAsync" />.
/// </remarks>
/// <typeparam name="TEntity">The entity type managed by this repository.</typeparam>
public interface IRepository<TEntity> : IRepository
    where TEntity : class
{
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
    ///     Retrieves an entity by matching its key property values against the provided
    ///     entity instance.
    /// </summary>
    /// <param name="entity">
    ///     An entity whose key properties are used to build the lookup. Only key values
    ///     are considered; other fields are ignored.
    /// </param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The matching entity or <see langword="null" />.</returns>
    ValueTask<TEntity?> GetAsync(TEntity? entity, CancellationToken ct = default);

    /// <summary>
    ///     Retrieves an entity by its keys and projects it to <typeparamref name="TResult" />.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="entity">An entity whose key properties are used to build the lookup.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask<TResult?> GetAsync<TResult>(TEntity? entity, CancellationToken ct = default);

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
    ///     Estimates the number of entities matching the predicate. Defaults to the exact
    ///     <see cref="LongCountAsync{TResult}" />; providers with cheaper statistics
    ///     (e.g. table cardinality estimates) can override.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="predicate">An optional query transformation.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask<long> EstimateCountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    ) {
        return LongCountAsync(predicate, ct);
    }

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
}
