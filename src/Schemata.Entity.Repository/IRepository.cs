using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Entity.Repository;

/// <summary>
///     Non-generic repository contract carrying the entity-agnostic surface (advisor
///     context, unit-of-work enlistment, and commit). Entity-agnostic coordination code
///     depends on this interface; entity-specific work uses
///     <see cref="IRepository{TEntity}" />, whose operations carry explicit
///     <see cref="QueryOptions" /> and <see cref="MutationOptions" />.
/// </summary>
public interface IRepository : IAsyncDisposable, IDisposable
{
    /// <summary>
    ///     Carries the advice context that gates advisor execution for this repository instance.
    /// </summary>
    AdviceContext AdviceContext { get; }

    /// <summary>
    ///     Begins a caller-owned unit of work bound to this repository's data context and enlists
    ///     this repository in it. The returned unit of work is one-shot and caller-owned: after
    ///     <see cref="IUnitOfWork.CommitAsync" /> / <see cref="IUnitOfWork.RollbackAsync" /> /
    ///     disposal it never reopens; resolve a fresh repository to start new work.
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
    ///     Persists all pending changes through the repository's implicit write unit of work and
    ///     reopens the repository for subsequent work. Throws when the repository is enlisted in a
    ///     caller-owned unit of work — commit that unit of work instead. A commit with no staged
    ///     writes sends no committed notification.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    Task CommitAsync(CancellationToken ct = default);
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
    /// <param name="options">Per-call query behavior overrides.</param>
    IAsyncEnumerable<TResult> ListAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct      = default,
        QueryOptions                                    options = default
    );

    /// <summary>
    ///     Retrieves an entity by matching its key property values against the provided
    ///     entity instance.
    /// </summary>
    /// <param name="entity">
    ///     An entity whose key properties provide the lookup values. The lookup ignores
    ///     non-key fields.
    /// </param>
    /// <param name="ct">A cancellation token.</param>
    /// <param name="options">Per-call query behavior overrides.</param>
    /// <returns>The matching entity or <see langword="null" />.</returns>
    ValueTask<TEntity?> GetAsync(TEntity? entity, CancellationToken ct = default, QueryOptions options = default);

    /// <summary>
    ///     Retrieves an entity by its keys and projects it to <typeparamref name="TResult" />.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="entity">An entity whose key properties provide the lookup values.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <param name="options">Per-call query behavior overrides.</param>
    ValueTask<TResult?> GetAsync<TResult>(TEntity? entity, CancellationToken ct = default, QueryOptions options = default);

    /// <summary>
    ///     Selects a direct immutable scalar property by primary key through build-query advisors.
    ///     EF Core supplies the admission value for the same still-Added entity in its unit of work.
    ///     Found distinguishes a null scalar from a missing or filtered row.
    /// </summary>
    ValueTask<OriginalResult<TResult>> GetOriginalAsync<TResult>(
        TEntity entity,
        Expression<Func<TEntity, TResult>> projection,
        CancellationToken                  ct      = default,
        QueryOptions                       options = default
    );

    /// <summary>
    ///     Finds an entity by its primary key values.
    /// </summary>
    /// <param name="keys">The primary key values in property declaration order.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <param name="options">Per-call query behavior overrides.</param>
    ValueTask<TEntity?> FindAsync(object[] keys, CancellationToken ct = default, QueryOptions options = default);

    /// <summary>
    ///     Finds an entity by its primary key values and projects it to
    ///     <typeparamref name="TResult" />.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="keys">The primary key values in property declaration order.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <param name="options">Per-call query behavior overrides.</param>
    ValueTask<TResult?> FindAsync<TResult>(object[] keys, CancellationToken ct = default, QueryOptions options = default);

    /// <summary>
    ///     Returns the first matching result or <see langword="null" />, after build-query
    ///     advisors are applied.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="predicate">An optional query transformation.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <param name="options">Per-call query behavior overrides.</param>
    ValueTask<TResult?> FirstOrDefaultAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct      = default,
        QueryOptions                                    options = default
    );

    /// <summary>
    ///     Returns the single matching result or <see langword="null" />, after build-query
    ///     advisors are applied.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="predicate">An optional query transformation.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <param name="options">Per-call query behavior overrides.</param>
    ValueTask<TResult?> SingleOrDefaultAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct      = default,
        QueryOptions                                    options = default
    );

    /// <summary>
    ///     Returns <see langword="true" /> if any entity matches the predicate, after
    ///     build-query advisors are applied.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="predicate">An optional query transformation.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <param name="options">Per-call query behavior overrides.</param>
    ValueTask<bool> AnyAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct      = default,
        QueryOptions                                    options = default
    );

    /// <summary>
    ///     Returns the count of entities matching the predicate, after build-query advisors
    ///     are applied.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="predicate">An optional query transformation.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <param name="options">Per-call query behavior overrides.</param>
    ValueTask<int> CountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct      = default,
        QueryOptions                                    options = default
    );

    /// <summary>
    ///     Returns the long count of entities matching the predicate, after build-query
    ///     advisors are applied.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="predicate">An optional query transformation.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <param name="options">Per-call query behavior overrides.</param>
    ValueTask<long> LongCountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct      = default,
        QueryOptions                                    options = default
    );

    /// <summary>
    ///     Estimates the number of results after query scoping. Returns <see langword="null" />
    ///     when the provider or query shape cannot supply an estimate.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="predicate">An optional query transformation.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <param name="options">Per-call query behavior overrides.</param>
    ValueTask<long?> EstimateCountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct      = default,
        QueryOptions                                    options = default
    ) {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult<long?>(null);
    }

    /// <summary>
    ///     Drives the entity through the add advisor pipeline
    ///     (see <see cref="IRepositoryAddAdvisor{TEntity}" />) before persistence.
    /// </summary>
    /// <param name="entity">The entity to persist.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <param name="options">Per-call mutation behavior overrides.</param>
    Task<MutationResult> AddAsync(TEntity entity, CancellationToken ct = default, MutationOptions options = default);

    /// <summary>
    ///     Drives each entity through the add advisor pipeline before persistence.
    /// </summary>
    /// <param name="entities">The entities to persist.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <param name="options">Per-call mutation behavior overrides.</param>
    Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default, MutationOptions options = default);

    /// <summary>
    ///     Drives the entity through the update advisor pipeline
    ///     (see <see cref="IRepositoryUpdateAdvisor{TEntity}" />) before persistence.
    /// </summary>
    /// <param name="entity">The entity to persist with updated values.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <param name="options">Per-call mutation behavior overrides.</param>
    Task<MutationResult> UpdateAsync(TEntity entity, CancellationToken ct = default, MutationOptions options = default);

    /// <summary>
    ///     Drives the entity through the remove advisor pipeline
    ///     (see <see cref="IRepositoryRemoveAdvisor{TEntity}" />). When soft-delete
    ///     is active and not suppressed, <see cref="AdviceRemoveSoftDelete{TEntity}" />
    ///     intercepts the deletion and converts it to a soft-delete update, per
    ///     <seealso href="https://google.aip.dev/164">AIP-164: Soft delete</seealso>.
    /// </summary>
    /// <param name="entity">The entity to remove.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <param name="options">Per-call mutation behavior overrides.</param>
    Task<MutationResult> RemoveAsync(TEntity entity, CancellationToken ct = default, MutationOptions options = default);

    /// <summary>
    ///     Drives each entity through the remove advisor pipeline.
    /// </summary>
    /// <param name="entities">The entities to remove.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <param name="options">Per-call mutation behavior overrides.</param>
    Task RemoveRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default, MutationOptions options = default);
}
