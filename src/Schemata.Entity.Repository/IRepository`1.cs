using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;

namespace Schemata.Entity.Repository;

/// <summary>
///     Generic repository interface providing strongly-typed CRUD operations with an advisor pipeline.
/// </summary>
/// <typeparam name="TEntity">The entity type managed by this repository.</typeparam>
public interface IRepository<TEntity>
    where TEntity : class
{
    /// <summary>
    ///     Gets the advice context used for advisor pipeline configuration on this repository instance.
    /// </summary>
    AdviceContext AdviceContext { get; }

    /// <summary>
    ///     Returns all entities as an async enumerable without applying the advisor pipeline.
    /// </summary>
    /// <returns>An async enumerable of all entities.</returns>
    IAsyncEnumerable<TEntity> AsAsyncEnumerable();

    /// <summary>
    ///     Returns all entities as a queryable without applying the advisor pipeline.
    /// </summary>
    /// <returns>A queryable of all entities.</returns>
    IQueryable<TEntity> AsQueryable();

    /// <summary>
    ///     Lists entities with the advisor pipeline (build-query advisors applied), projected by the predicate.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="predicate">An optional query transformation.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An async enumerable of projected results.</returns>
    IAsyncEnumerable<TResult> ListAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    );

    /// <summary>
    ///     Searches entities with the advisor pipeline (provider-specific full-text search).
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="predicate">An optional query transformation.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An async enumerable of projected results.</returns>
    IAsyncEnumerable<TResult> SearchAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    );

    /// <summary>
    ///     Gets an entity by its key properties.
    /// </summary>
    /// <param name="entity">An entity instance whose key properties identify the record to retrieve.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The matching entity or <see langword="null" />.</returns>
    ValueTask<TEntity?> GetAsync(TEntity entity, CancellationToken ct = default);

    /// <summary>
    ///     Gets an entity by its key properties, projected to <typeparamref name="TResult" />.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="entity">An entity instance whose key properties identify the record to retrieve.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The matching result or <see langword="null" />.</returns>
    ValueTask<TResult?> GetAsync<TResult>(TEntity entity, CancellationToken ct = default);

    /// <summary>
    ///     Finds an entity by its primary key values.
    /// </summary>
    /// <param name="keys">The primary key values in property declaration order.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The matching entity or <see langword="null" />.</returns>
    ValueTask<TEntity?> FindAsync(object[] keys, CancellationToken ct = default);

    /// <summary>
    ///     Finds an entity by its primary key values, projected to <typeparamref name="TResult" />.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="keys">The primary key values in property declaration order.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The matching result or <see langword="null" />.</returns>
    ValueTask<TResult?> FindAsync<TResult>(object[] keys, CancellationToken ct = default);

    /// <summary>
    ///     Returns the first entity matching the predicate, or <see langword="null" />.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="predicate">An optional query transformation.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The first matching result or <see langword="null" />.</returns>
    ValueTask<TResult?> FirstOrDefaultAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    );

    /// <summary>
    ///     Returns the single entity matching the predicate, or <see langword="null" />.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="predicate">An optional query transformation.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The single matching result or <see langword="null" />.</returns>
    ValueTask<TResult?> SingleOrDefaultAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    );

    /// <summary>
    ///     Returns whether any entity matches the predicate.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="predicate">An optional query transformation.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns><see langword="true" /> if at least one entity matches.</returns>
    ValueTask<bool> AnyAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    );

    /// <summary>
    ///     Returns the count of entities matching the predicate.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="predicate">An optional query transformation.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The number of matching entities.</returns>
    ValueTask<int> CountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    );

    /// <summary>
    ///     Returns the long count of entities matching the predicate.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="predicate">An optional query transformation.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The number of matching entities as <see cref="long" />.</returns>
    ValueTask<long> LongCountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    );

    /// <summary>
    ///     Adds an entity through the advisor pipeline.
    /// </summary>
    /// <param name="entity">The entity to add.</param>
    /// <param name="ct">A cancellation token.</param>
    Task AddAsync(TEntity entity, CancellationToken ct = default);

    /// <summary>
    ///     Adds multiple entities through the advisor pipeline.
    /// </summary>
    /// <param name="entities">The entities to add.</param>
    /// <param name="ct">A cancellation token.</param>
    Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default);

    /// <summary>
    ///     Updates an entity through the advisor pipeline.
    /// </summary>
    /// <param name="entity">The entity to update.</param>
    /// <param name="ct">A cancellation token.</param>
    Task UpdateAsync(TEntity entity, CancellationToken ct = default);

    /// <summary>
    ///     Removes an entity through the advisor pipeline.
    /// </summary>
    /// <param name="entity">The entity to remove.</param>
    /// <param name="ct">A cancellation token.</param>
    Task RemoveAsync(TEntity entity, CancellationToken ct = default);

    /// <summary>
    ///     Removes multiple entities through the advisor pipeline.
    /// </summary>
    /// <param name="entities">The entities to remove.</param>
    /// <param name="ct">A cancellation token.</param>
    Task RemoveRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default);

    /// <summary>
    ///     Commits pending changes to the data store.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The number of rows affected.</returns>
    ValueTask<int> CommitAsync(CancellationToken ct = default);

    /// <summary>
    ///     Detaches the entity from the change tracker.
    /// </summary>
    /// <param name="entity">The entity to detach.</param>
    void Detach(TEntity entity);

    /// <summary>
    ///     Creates a new repository instance with a fresh advice context, isolating subsequent suppress calls.
    /// </summary>
    /// <returns>A new <see cref="IRepository{TEntity}" /> instance.</returns>
    IRepository<TEntity> Once();

    /// <summary>
    ///     Suppresses validation during add operations for this repository instance.
    /// </summary>
    /// <returns>This repository instance for chaining.</returns>
    IRepository<TEntity> SuppressAddValidation();

    /// <summary>
    ///     Suppresses validation during update operations for this repository instance.
    /// </summary>
    /// <returns>This repository instance for chaining.</returns>
    IRepository<TEntity> SuppressUpdateValidation();

    /// <summary>
    ///     Suppresses concurrency-stamp checks for this repository instance.
    /// </summary>
    /// <returns>This repository instance for chaining.</returns>
    IRepository<TEntity> SuppressConcurrency();

    /// <summary>
    ///     Suppresses the soft-delete query filter for this repository instance.
    /// </summary>
    /// <returns>This repository instance for chaining.</returns>
    IRepository<TEntity> SuppressQuerySoftDelete();

    /// <summary>
    ///     Suppresses soft-delete behavior on add and remove for this repository instance.
    /// </summary>
    /// <returns>This repository instance for chaining.</returns>
    IRepository<TEntity> SuppressSoftDelete();

    /// <summary>
    ///     Suppresses automatic timestamp assignment for this repository instance.
    /// </summary>
    /// <returns>This repository instance for chaining.</returns>
    IRepository<TEntity> SuppressTimestamp();
}
