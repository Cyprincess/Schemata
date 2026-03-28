using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;

namespace Schemata.Entity.Repository;

/// <summary>
///     Non-generic repository interface for type-erased entity access.
/// </summary>
public interface IRepository
{
    /// <summary>
    ///     Gets the advice context used for advisor pipeline configuration on this repository instance.
    /// </summary>
    AdviceContext AdviceContext { get; }

    /// <summary>
    ///     Lists entities matching the predicate, applying build-query advisors.
    /// </summary>
    /// <typeparam name="T">The entity type to query.</typeparam>
    /// <param name="predicate">An optional filter expression.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An async enumerable of matching entities as <see cref="object" />.</returns>
    IAsyncEnumerable<object> ListAsync<T>(Expression<Func<T, bool>>? predicate, CancellationToken ct = default);

    /// <summary>
    ///     Searches entities matching the predicate (full-text or provider-specific search).
    /// </summary>
    /// <typeparam name="T">The entity type to search.</typeparam>
    /// <param name="predicate">An optional filter expression.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An async enumerable of matching entities as <see cref="object" />.</returns>
    IAsyncEnumerable<object> SearchAsync<T>(Expression<Func<T, bool>>? predicate, CancellationToken ct = default);

    /// <summary>
    ///     Returns the first entity matching the predicate, or <see langword="null" />.
    /// </summary>
    /// <typeparam name="T">The entity type to query.</typeparam>
    /// <param name="predicate">An optional filter expression.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The first matching entity or <see langword="null" />.</returns>
    ValueTask<object?> FirstOrDefaultAsync<T>(Expression<Func<T, bool>>? predicate, CancellationToken ct = default);

    /// <summary>
    ///     Returns the single entity matching the predicate, or <see langword="null" />.
    /// </summary>
    /// <typeparam name="T">The entity type to query.</typeparam>
    /// <param name="predicate">An optional filter expression.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The single matching entity or <see langword="null" />.</returns>
    ValueTask<object?> SingleOrDefaultAsync<T>(Expression<Func<T, bool>>? predicate, CancellationToken ct = default);

    /// <summary>
    ///     Returns whether any entity matches the predicate.
    /// </summary>
    /// <typeparam name="T">The entity type to query.</typeparam>
    /// <param name="predicate">An optional filter expression.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns><see langword="true" /> if at least one entity matches.</returns>
    ValueTask<bool> AnyAsync<T>(Expression<Func<T, bool>>? predicate, CancellationToken ct = default);

    /// <summary>
    ///     Returns the count of entities matching the predicate.
    /// </summary>
    /// <typeparam name="T">The entity type to query.</typeparam>
    /// <param name="predicate">An optional filter expression.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The number of matching entities.</returns>
    ValueTask<int> CountAsync<T>(Expression<Func<T, bool>>? predicate, CancellationToken ct = default);

    /// <summary>
    ///     Returns the long count of entities matching the predicate.
    /// </summary>
    /// <typeparam name="T">The entity type to query.</typeparam>
    /// <param name="predicate">An optional filter expression.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The number of matching entities as <see cref="long" />.</returns>
    ValueTask<long> LongCountAsync<T>(Expression<Func<T, bool>>? predicate, CancellationToken ct = default);

    /// <summary>
    ///     Adds an entity through the advisor pipeline.
    /// </summary>
    /// <param name="entity">The entity to add.</param>
    /// <param name="ct">A cancellation token.</param>
    Task AddAsync(object entity, CancellationToken ct = default);

    /// <summary>
    ///     Updates an entity through the advisor pipeline.
    /// </summary>
    /// <param name="entity">The entity to update.</param>
    /// <param name="ct">A cancellation token.</param>
    Task UpdateAsync(object entity, CancellationToken ct = default);

    /// <summary>
    ///     Removes an entity through the advisor pipeline.
    /// </summary>
    /// <param name="entity">The entity to remove.</param>
    /// <param name="ct">A cancellation token.</param>
    Task RemoveAsync(object entity, CancellationToken ct = default);

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
    void Detach(object entity);

    /// <summary>
    ///     Creates a new repository instance with a fresh advice context, isolating subsequent suppress calls.
    /// </summary>
    /// <returns>A new <see cref="IRepository" /> instance.</returns>
    IRepository Once();

    /// <summary>
    ///     Suppresses validation during add operations for this repository instance.
    /// </summary>
    /// <returns>This repository instance for chaining.</returns>
    IRepository SuppressAddValidation();

    /// <summary>
    ///     Suppresses validation during update operations for this repository instance.
    /// </summary>
    /// <returns>This repository instance for chaining.</returns>
    IRepository SuppressUpdateValidation();

    /// <summary>
    ///     Suppresses concurrency-stamp checks for this repository instance.
    /// </summary>
    /// <returns>This repository instance for chaining.</returns>
    IRepository SuppressConcurrency();

    /// <summary>
    ///     Suppresses the soft-delete query filter for this repository instance.
    /// </summary>
    /// <returns>This repository instance for chaining.</returns>
    IRepository SuppressQuerySoftDelete();

    /// <summary>
    ///     Suppresses soft-delete behavior on add and remove for this repository instance.
    /// </summary>
    /// <returns>This repository instance for chaining.</returns>
    IRepository SuppressSoftDelete();

    /// <summary>
    ///     Suppresses automatic timestamp assignment for this repository instance.
    /// </summary>
    /// <returns>This repository instance for chaining.</returns>
    IRepository SuppressTimestamp();
}
