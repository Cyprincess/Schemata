using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Advice;
using Schemata.Common;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Entity.Repository;

/// <summary>
///     Non-generic base class providing shared key-property caching logic for repository
///     implementations.
/// </summary>
public abstract class RepositoryBase
{
    private static readonly ConcurrentDictionary<RuntimeTypeHandle, IReadOnlyList<PropertyInfo>> KeyProperties = new();

    /// <summary>
    ///     Resolves the key properties for the specified type by reading the class-level
    ///     <see cref="PrimaryKeyAttribute" /> (EF Core 7+), then falling back to the
    ///     <see cref="IIdentifier.Uid" /> convention when no attribute is declared.
    /// </summary>
    /// <param name="type">The entity type to inspect.</param>
    /// <returns>The list of key property infos.</returns>
    public static IReadOnlyList<PropertyInfo> ResolveKeyProperties(Type type) {
        var properties = TypePropertiesCache(type);

        var primary = type.GetCustomAttribute<PrimaryKeyAttribute>(true);
        if (primary is not null) {
            var map      = properties.ToDictionary(p => p.Name, StringComparer.Ordinal);
            var resolved = new List<PropertyInfo>();
            foreach (var name in primary.PropertyNames) {
                if (map.TryGetValue(name, out var prop)) {
                    resolved.Add(prop);
                } else {
                    resolved.Clear();
                    break;
                }
            }

            if (resolved.Count > 0) {
                return resolved;
            }
        }

        var id = properties.FirstOrDefault(p => string.Equals(p.Name, nameof(IIdentifier.Uid), StringComparison.InvariantCultureIgnoreCase));
        if (id is not null) {
            return new List<PropertyInfo> { id };
        }

        return new List<PropertyInfo>();
    }

    /// <summary>
    ///     Returns the cached list of key properties for the specified type.
    /// </summary>
    /// <param name="type">The entity type to inspect.</param>
    /// <returns>The list of key property infos.</returns>
    public static IReadOnlyList<PropertyInfo> KeyPropertiesCache(Type type) {
        if (KeyProperties.TryGetValue(type.TypeHandle, out var properties)) {
            return properties;
        }

        properties = ResolveKeyProperties(type);

        KeyProperties[type.TypeHandle] = properties;

        return properties;
    }

    protected static IReadOnlyList<PropertyInfo> TypePropertiesCache(Type type) {
        var properties = AppDomainTypeCache.GetProperties(type).Values.Where(IsMapped).ToList();
        return properties;
    }

    private static bool IsMapped(PropertyInfo property) {
        if (!property.CanRead) {
            return false;
        }

        var getter = property.GetGetMethod();
        if (getter is null) {
            return false;
        }

        return !property.HasCustomAttribute<NotMappedAttribute>(false);
    }
}

/// <summary>
///     Abstract base class for repository implementations providing advisor pipeline
///     integration and common CRUD logic. All query methods pass through
///     <see cref="IRepositoryBuildQueryAdvisor{TEntity}" />; mutate methods delegate
///     to abstract/virtual methods that concrete providers must implement.
/// </summary>
/// <typeparam name="TEntity">The entity type managed by this repository.</typeparam>
public abstract class RepositoryBase<TEntity> : RepositoryBase, IRepository<TEntity>
    where TEntity : class
{
    /// <summary>
    ///     <see langword="true" /> when <typeparamref name="TEntity" /> is <see cref="IConcurrency" />
    ///     and its <see cref="IConcurrency.Timestamp" /> carries <see cref="ConcurrencyCheckAttribute" />,
    ///     enabling provider-level optimistic concurrency on update. Consumers opt in by annotating
    ///     the concrete entity property; the attribute on the interface alone does not propagate.
    /// </summary>
    protected static readonly bool IsConcurrencyControlled = typeof(IConcurrency).IsAssignableFrom(typeof(TEntity))
                                                          && typeof(TEntity).GetProperty(nameof(IConcurrency.Timestamp))?.GetCustomAttribute<ConcurrencyCheckAttribute>(true) is not null;

    private readonly List<TEntity> _added   = [];
    private readonly List<TEntity> _removed = [];
    private readonly List<TEntity> _updated = [];

    private bool _completed;
    private bool _disposed;

    private IUnitOfWork? _uow;

    protected RepositoryBase(IServiceProvider sp) {
        ServiceProvider = sp;
        AdviceContext   = new(sp);
    }

    protected virtual IServiceProvider ServiceProvider { get; }

    /// <summary><see langword="true" /> when the repository owns its context's lifetime.</summary>
    protected bool OwnsContext { get; set; } = true;

    #region IRepository<TEntity> Members

    public virtual AdviceContext AdviceContext { get; }

    public virtual async IAsyncEnumerable<TResult> ListAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        [EnumeratorCancellation] CancellationToken      ct = default
    ) {
        var query = await BuildQueryAsync(predicate, ct);

        var enumerable = AsAsyncEnumerable(query, ct);

        await foreach (var entity in enumerable) {
            ct.ThrowIfCancellationRequested();
            yield return entity;
        }
    }

    public virtual ValueTask<TEntity?> GetAsync(TEntity? entity, CancellationToken ct = default) {
        return GetAsync<TEntity>(entity, ct);
    }

    public virtual ValueTask<TResult?> GetAsync<TResult>(TEntity? entity, CancellationToken ct = default) {
        if (entity is null) {
            return ValueTask.FromResult(default(TResult));
        }

        var type = entity.GetType();

        var properties = KeyPropertiesCache(type);
        if (properties.Count == 0) {
            throw new ArgumentException(SchemataResources.GetResourceString(SchemataResources.ST1020));
        }

        var keys = new List<object>();
        foreach (var property in properties) {
            var value = property.GetValue(entity);
            if (value is null) {
                return ValueTask.FromResult(default(TResult));
            }

            keys.Add(value);
        }

        return FindAsync<TResult>(keys.ToArray(), ct);
    }

    public virtual ValueTask<TEntity?> FindAsync(object[] keys, CancellationToken ct = default) {
        return FindAsync<TEntity>(keys, ct);
    }

    public virtual async ValueTask<TResult?> FindAsync<TResult>(object[] keys, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        var type = typeof(TEntity);

        var properties = KeyPropertiesCache(type);
        if (properties.Count == 0) {
            throw new ArgumentException(SchemataResources.GetResourceString(SchemataResources.ST1020));
        }

        if (properties.Count != keys.Length) {
            throw new ArgumentException(SchemataResources.GetResourceString(SchemataResources.ST1022));
        }

        var predicate = Predicate.True<TEntity>();

        var instance = Expression.Parameter(type, "e");

        for (var i = 0; i < properties.Count; i++) {
            var info     = properties[i];
            var property = Expression.Property(instance, info);
            var value    = Expression.Constant(keys[i]);
            var equality = Expression.Equal(property, value);

            var lambda = Expression.Lambda<Func<TEntity, bool>>(equality, instance);

            predicate = predicate.And(lambda);
        }

        return await SingleOrDefaultAsync<TResult>(q => q.Where(predicate).OfType<TResult>(), ct);
    }

    public virtual async ValueTask<TResult?> FirstOrDefaultAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    ) {
        var query = await BuildQueryAsync(predicate, ct);

        var context = new QueryContext<TEntity, TResult, TResult>(this, query);

        switch (await Advisor.For<IRepositoryQueryAdvisor<TEntity, TResult, TResult>>()
                             .RunAsync(AdviceContext, context, ct)) {
            case AdviseResult.Block:
                return default;
            case AdviseResult.Handle:
                return context.Result;
            case AdviseResult.Continue:
            default:
                break;
        }

        context.Result = await FirstOrDefaultAsync(query, ct);

        switch (await Advisor.For<IRepositoryResultAdvisor<TEntity, TResult, TResult>>()
                             .RunAsync(AdviceContext, context, ct)) {
            case AdviseResult.Block:
                return default;
            case AdviseResult.Handle:
            case AdviseResult.Continue:
            default:
                break;
        }

        return context.Result;
    }

    public virtual async ValueTask<TResult?> SingleOrDefaultAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    ) {
        var query = await BuildQueryAsync(predicate, ct);

        var context = new QueryContext<TEntity, TResult, TResult>(this, query);

        switch (await Advisor.For<IRepositoryQueryAdvisor<TEntity, TResult, TResult>>()
                             .RunAsync(AdviceContext, context, ct)) {
            case AdviseResult.Block:
                return default;
            case AdviseResult.Handle:
                return context.Result;
            case AdviseResult.Continue:
            default:
                break;
        }

        context.Result = await SingleOrDefaultAsync(query, ct);

        switch (await Advisor.For<IRepositoryResultAdvisor<TEntity, TResult, TResult>>()
                             .RunAsync(AdviceContext, context, ct)) {
            case AdviseResult.Block:
                return default;
            case AdviseResult.Handle:
            case AdviseResult.Continue:
            default:
                break;
        }

        return context.Result;
    }

    public virtual async ValueTask<bool> AnyAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    ) {
        var query = await BuildQueryAsync(predicate, ct);

        var context = new QueryContext<TEntity, TResult, bool>(this, query);

        switch (await Advisor.For<IRepositoryQueryAdvisor<TEntity, TResult, bool>>()
                             .RunAsync(AdviceContext, context, ct)) {
            case AdviseResult.Block:
                return false;
            case AdviseResult.Handle:
                return context.Result;
            case AdviseResult.Continue:
            default:
                break;
        }

        context.Result = await AnyAsync(query, ct);

        switch (await Advisor.For<IRepositoryResultAdvisor<TEntity, TResult, bool>>()
                             .RunAsync(AdviceContext, context, ct)) {
            case AdviseResult.Block:
                return false;
            case AdviseResult.Handle:
            case AdviseResult.Continue:
            default:
                break;
        }

        return context.Result;
    }

    public virtual async ValueTask<int> CountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    ) {
        var query = await BuildQueryAsync(predicate, ct);

        var context = new QueryContext<TEntity, TResult, int>(this, query);

        switch (await Advisor.For<IRepositoryQueryAdvisor<TEntity, TResult, int>>()
                             .RunAsync(AdviceContext, context, ct)) {
            case AdviseResult.Block:
                return 0;
            case AdviseResult.Handle:
                return context.Result;
            case AdviseResult.Continue:
            default:
                break;
        }

        context.Result = await CountAsync(query, ct);

        switch (await Advisor.For<IRepositoryResultAdvisor<TEntity, TResult, int>>()
                             .RunAsync(AdviceContext, context, ct)) {
            case AdviseResult.Block:
                return 0;
            case AdviseResult.Handle:
            case AdviseResult.Continue:
            default:
                break;
        }

        return context.Result;
    }

    public virtual async ValueTask<long> LongCountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    ) {
        var query = await BuildQueryAsync(predicate, ct);

        var context = new QueryContext<TEntity, TResult, long>(this, query);

        switch (await Advisor.For<IRepositoryQueryAdvisor<TEntity, TResult, long>>()
                             .RunAsync(AdviceContext, context, ct)) {
            case AdviseResult.Block:
                return 0;
            case AdviseResult.Handle:
                return context.Result;
            case AdviseResult.Continue:
            default:
                break;
        }

        context.Result = await LongCountAsync(query, ct);

        switch (await Advisor.For<IRepositoryResultAdvisor<TEntity, TResult, long>>()
                             .RunAsync(AdviceContext, context, ct)) {
            case AdviseResult.Block:
                return 0;
            case AdviseResult.Handle:
            case AdviseResult.Continue:
            default:
                break;
        }

        return context.Result;
    }

    public abstract Task AddAsync(TEntity entity, CancellationToken ct = default);

    public virtual async Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default) {
        foreach (var entity in entities) {
            ct.ThrowIfCancellationRequested();
            await AddAsync(entity, ct);
        }
    }

    public abstract Task UpdateAsync(TEntity entity, CancellationToken ct = default);

    public abstract Task RemoveAsync(TEntity entity, CancellationToken ct = default);

    public virtual async Task RemoveRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default) {
        foreach (var entity in entities) {
            ct.ThrowIfCancellationRequested();
            await RemoveAsync(entity, ct);
        }
    }

    /// <inheritdoc cref="IRepository.CommitAsync" />
    public virtual async Task CommitAsync(CancellationToken ct = default) {
        if (_completed) {
            throw new InvalidOperationException("Repository's unit of work has already completed. Resolve a fresh IRepository<T> to start new work.");
        }

        if (_uow is not null) {
            await _uow.CommitAsync(ct);
            return;
        }

        if (!OwnsContext) {
            throw new InvalidOperationException("Repository is enlisted in a unit of work. Call IUnitOfWork.CommitAsync instead.");
        }

        // Owned but never mutated: a degenerate commit. Dispatch the empty snapshot so committed
        // advisors observe the no-op commit on the same footing as the enlisted path.
        await DispatchCommittedAsync(SnapshotChanges(), ct);
        _completed = true;
    }

    public virtual IDisposable SuppressAddValidation()    { return AdviceContext.Use<AddValidationSuppressed>(); }
    public virtual IDisposable SuppressUpdateValidation() { return AdviceContext.Use<UpdateValidationSuppressed>(); }
    public virtual IDisposable SuppressQuerySoftDelete()  { return AdviceContext.Use<QuerySoftDeleteSuppressed>(); }
    public virtual IDisposable SuppressSoftDelete()       { return AdviceContext.Use<SoftDeleteSuppressed>(); }
    public virtual IDisposable SuppressTimestamp()        { return AdviceContext.Use<TimestampSuppressed>(); }

    public virtual IUnitOfWork Begin() {
        var uow = CreateUnitOfWork();

        Join(uow);

        return uow;
    }

    public virtual void Join(IUnitOfWork uow) {
        ArgumentNullException.ThrowIfNull(uow);
        if (!OwnsContext) {
            throw new InvalidOperationException("Repository is already enlisted in a unit of work.");
        }

        if (_added.Count > 0 || _updated.Count > 0 || _removed.Count > 0) {
            throw new InvalidOperationException("Cannot enlist a repository with uncommitted work. "
                                              + "Call CommitAsync before Join, or resolve a fresh IRepository<T> instance.");
        }

        Enlist(uow);
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;

        // Dispose the implicit unit of work (rolling back when it was never committed); fall back to
        // the owned read context when no mutation enlisted one. An externally joined unit of work is
        // the caller's to dispose, so do nothing in that case.
        if (_uow is not null) {
            _uow.Dispose();
        } else if (OwnsContext) {
            DisposeContext();
        }

        _added.Clear();
        _updated.Clear();
        _removed.Clear();

        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync() {
        if (_disposed) return;
        _disposed = true;

        if (_uow is not null) {
            await _uow.DisposeAsync();
        } else if (OwnsContext) {
            await DisposeContextAsync();
        }

        _added.Clear();
        _updated.Clear();
        _removed.Clear();

        GC.SuppressFinalize(this);
    }

    #endregion

    protected abstract ConfiguredCancelableAsyncEnumerable<TResult> AsAsyncEnumerable<TResult>(
        IQueryable<TResult> query,
        CancellationToken   ct
    );

    protected virtual Task<TResult?> FirstOrDefaultAsync<TResult>(IQueryable<TResult> query, CancellationToken ct) {
        throw new NotImplementedException();
    }

    protected virtual Task<TResult?> SingleOrDefaultAsync<TResult>(IQueryable<TResult> query, CancellationToken ct) {
        throw new NotImplementedException();
    }

    protected virtual Task<bool> AnyAsync<TResult>(IQueryable<TResult> query, CancellationToken ct) {
        throw new NotImplementedException();
    }

    protected virtual Task<int> CountAsync<TResult>(IQueryable<TResult> query, CancellationToken ct) {
        throw new NotImplementedException();
    }

    protected virtual Task<long> LongCountAsync<TResult>(IQueryable<TResult> query, CancellationToken ct) {
        throw new NotImplementedException();
    }

    /// <summary>
    ///     Captures the current add/update/remove tracking lists into a snapshot and then clears
    ///     them so the next commit starts with empty lists.
    /// </summary>
    protected CommitChanges<TEntity> SnapshotChanges() {
        var snapshot = new CommitChanges<TEntity> {
            Added = _added.ToArray(), Updated = _updated.ToArray(), Removed = _removed.ToArray(),
        };

        _added.Clear();
        _updated.Clear();
        _removed.Clear();

        return snapshot;
    }

    /// <summary>
    ///     Runs the <see cref="IRepositoryCommittedAdvisor{TEntity}" /> pipeline against the
    ///     given change snapshot.
    /// </summary>
    protected async Task DispatchCommittedAsync(CommitChanges<TEntity> changes, CancellationToken ct) {
        await Advisor.For<IRepositoryCommittedAdvisor<TEntity>>()
                     .RunAsync(AdviceContext, this, changes, ct);
    }

    /// <summary>
    ///     Clears the add/update/remove tracking lists without dispatching. Called on rollback.
    /// </summary>
    protected void ResetTracking() {
        _added.Clear();
        _updated.Clear();
        _removed.Clear();
    }

    /// <summary>
    ///     Rollback callback enlisted with the unit of work. Discards the pending change snapshot
    ///     so a rolled-back commit does not replay its tracking on a later commit.
    /// </summary>
    protected virtual void OnRollback() { ResetTracking(); }

    /// <summary>
    ///     Ensures the repository writes through a unit of work before its first standalone
    ///     mutation. When the repository still owns its context, this enlists a fresh
    ///     repository-owned unit of work (created by <see cref="CreateUnitOfWork" />); once the
    ///     repository is enlisted (here or via <see cref="Join" />) it is a no-op.
    /// </summary>
    protected void EnsureWriteUnitOfWork() {
        if (_completed) {
            throw new InvalidOperationException("Repository's unit of work has already completed. Resolve a fresh IRepository<T> to start new work.");
        }

        if (!OwnsContext) {
            return;
        }

        _uow = CreateUnitOfWork();
        Enlist(_uow);
    }

    /// <summary>
    ///     Swaps to the unit of work's context, registers the commit and rollback sinks, and marks
    ///     the repository as enlisted. Shared by <see cref="Join" /> and
    ///     <see cref="EnsureWriteUnitOfWork" />.
    /// </summary>
    private void Enlist(IUnitOfWork uow) {
        AttachContext(uow);

        if (uow is IUnitOfWorkSink registry) {
            registry.AddCommitSink(ct => {
                _completed = true;
                return DispatchCommittedAsync(SnapshotChanges(), ct);
            });
            registry.AddRollbackSink(() => {
                OnRollback();
                _completed = true;
            });
        }

        OwnsContext = false;
    }

    /// <summary>
    ///     Runs the <see cref="IRepositoryAddAdvisor{TEntity}" /> chain for a single entity and
    ///     stages it for the committed-advisor snapshot. Returns <see langword="false" /> when an
    ///     advisor blocked or handled the add, so callers skip persistence.
    /// </summary>
    protected async Task<bool> RunAddAdvisorsAsync(TEntity entity, CancellationToken ct) {
        switch (await Advisor.For<IRepositoryAddAdvisor<TEntity>>()
                             .RunAsync(AdviceContext, this, entity, ct)) {
            case AdviseResult.Block:
            case AdviseResult.Handle:
                return false;
            case AdviseResult.Continue:
            default:
                break;
        }

        TrackAdd(entity);

        return true;
    }

    protected void TrackAdd(TEntity entity) { _added.Add(entity); }

    protected void TrackUpdate(TEntity entity) { _updated.Add(entity); }

    protected void TrackRemove(TEntity entity) { _removed.Add(entity); }

    protected abstract IQueryable<TEntity> AsQueryable();

    /// <summary>
    ///     Creates the provider's unit of work for this repository's context. Used by
    ///     <see cref="Begin" /> and by <see cref="EnsureWriteUnitOfWork" /> to back standalone
    ///     writes.
    /// </summary>
    protected abstract IUnitOfWork CreateUnitOfWork();

    /// <summary>
    ///     Replaces the repository's owned context with the unit of work's context. The base wires
    ///     the commit and rollback sinks and sets <see cref="OwnsContext" /> to
    ///     <see langword="false" /> after this call returns.
    /// </summary>
    protected abstract void AttachContext(IUnitOfWork uow);

    protected abstract Task<IQueryable<TResult>> BuildQueryAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct
    );

    protected virtual QueryContainer<TEntity> AsQueryContainer() {
        var query = AsQueryable();

        var container = new QueryContainer<TEntity>(this, query);

        return container;
    }

    protected virtual IQueryable<TResult> BuildQuery<TResult>(
        IQueryable<TEntity>                             query,
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate
    ) {
        if (predicate is not null) {
            return predicate(query);
        }

        return query.OfType<TResult>();
    }

    protected abstract void DisposeContext();

    protected virtual ValueTask DisposeContextAsync() {
        DisposeContext();
        return ValueTask.CompletedTask;
    }
}
