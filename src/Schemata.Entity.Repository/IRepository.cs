using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Entity.Repository;

/// <summary>
///     Type-erased repository interface providing a common service type for dependency
///     injection. Members mirror <see cref="IRepository{TEntity}" /> with untyped signatures.
/// </summary>
public interface IRepository
{
    /// <summary>
    ///     Carries the advice context that gates advisor execution and holds suppression
    ///     flags for this repository instance.
    /// </summary>
    AdviceContext AdviceContext { get; }

    /// <summary>
    ///     Enumerates entities through the build-query advisor pipeline
    ///     (see <see cref="IRepositoryBuildQueryAdvisor{TEntity}" />), returning results
    ///     as untyped objects.
    /// </summary>
    /// <typeparam name="T">The entity type to query.</typeparam>
    /// <param name="predicate">
    ///     An optional filter expression. When <see langword="null" />, all entities of type
    ///     <typeparamref name="T" /> are returned.
    /// </param>
    /// <param name="ct">A cancellation token.</param>
    IAsyncEnumerable<object> ListAsync<T>(Expression<Func<T, bool>>? predicate, CancellationToken ct = default);

    /// <summary>
    ///     Searches entities through the build-query advisor pipeline, using provider-specific
    ///     full-text search when available. Falls back to <see cref="ListAsync{T}" /> otherwise.
    /// </summary>
    /// <typeparam name="T">The entity type to search.</typeparam>
    /// <param name="predicate">An optional filter expression.</param>
    /// <param name="ct">A cancellation token.</param>
    IAsyncEnumerable<object> SearchAsync<T>(Expression<Func<T, bool>>? predicate, CancellationToken ct = default);

    /// <summary>
    ///     Returns the first matching entity or <see langword="null" />, applying build-query
    ///     advisors.
    /// </summary>
    /// <typeparam name="T">The entity type to query.</typeparam>
    /// <param name="predicate">An optional filter expression.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask<object?> FirstOrDefaultAsync<T>(Expression<Func<T, bool>>? predicate, CancellationToken ct = default);

    /// <summary>
    ///     Returns the single matching entity or <see langword="null" />, applying build-query
    ///     advisors.
    /// </summary>
    /// <typeparam name="T">The entity type to query.</typeparam>
    /// <param name="predicate">An optional filter expression.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask<object?> SingleOrDefaultAsync<T>(Expression<Func<T, bool>>? predicate, CancellationToken ct = default);

    /// <summary>
    ///     Returns <see langword="true" /> if any entity matches the predicate, after build-query
    ///     advisors are applied.
    /// </summary>
    /// <typeparam name="T">The entity type to query.</typeparam>
    /// <param name="predicate">An optional filter expression.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask<bool> AnyAsync<T>(Expression<Func<T, bool>>? predicate, CancellationToken ct = default);

    /// <summary>
    ///     Returns the count of entities matching the predicate, after build-query advisors
    ///     are applied.
    /// </summary>
    /// <typeparam name="T">The entity type to query.</typeparam>
    /// <param name="predicate">An optional filter expression.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask<int> CountAsync<T>(Expression<Func<T, bool>>? predicate, CancellationToken ct = default);

    /// <summary>
    ///     Returns the long count of entities matching the predicate, after build-query
    ///     advisors are applied.
    /// </summary>
    /// <typeparam name="T">The entity type to query.</typeparam>
    /// <param name="predicate">An optional filter expression.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask<long> LongCountAsync<T>(Expression<Func<T, bool>>? predicate, CancellationToken ct = default);

    /// <summary>
    ///     Drives the entity through the add advisor pipeline
    ///     (see <see cref="IRepositoryAddAdvisor{TEntity}" />) before persistence.
    /// </summary>
    /// <param name="entity">The entity to persist.</param>
    /// <param name="ct">A cancellation token.</param>
    Task AddAsync(object entity, CancellationToken ct = default);

    /// <summary>
    ///     Drives the entity through the update advisor pipeline
    ///     (see <see cref="IRepositoryUpdateAdvisor{TEntity}" />) before persistence.
    /// </summary>
    /// <param name="entity">The entity to persist.</param>
    /// <param name="ct">A cancellation token.</param>
    Task UpdateAsync(object entity, CancellationToken ct = default);

    /// <summary>
    ///     Drives the entity through the remove advisor pipeline
    ///     (see <see cref="IRepositoryRemoveAdvisor{TEntity}" />). When soft-delete
    ///     is active and not suppressed, <see cref="AdviceRemoveSoftDelete{TEntity}" />
    ///     intercepts the deletion and converts it to a soft-delete update, per
    ///     <seealso href="https://google.aip.dev/164">AIP-164: Soft delete</seealso>.
    /// </summary>
    /// <param name="entity">The entity to remove.</param>
    /// <param name="ct">A cancellation token.</param>
    Task RemoveAsync(object entity, CancellationToken ct = default);

    /// <summary>
    ///     Begins a new unit of work for this repository, creating a database transaction
    ///     that coordinates all subsequent operations on this and related repositories.
    /// </summary>
    /// <returns>A disposable unit of work that must be committed or rolled back.</returns>
    IUnitOfWork BeginWork();

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
    void Detach(object entity);

    /// <summary>
    ///     Creates a new repository instance with a fresh <see cref="AdviceContext" />,
    ///     isolating any subsequent <c>Suppress*</c> calls.
    /// </summary>
    /// <returns>A new <see cref="IRepository" /> instance.</returns>
    IRepository Once();

    IRepository SuppressAddValidation();

    IRepository SuppressUpdateValidation();

    IRepository SuppressConcurrency();

    IRepository SuppressQuerySoftDelete();

    IRepository SuppressSoftDelete();

    IRepository SuppressTimestamp();
}
