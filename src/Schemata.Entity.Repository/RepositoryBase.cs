using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
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
    private readonly List<TEntity> _added   = [];
    private readonly List<TEntity> _removed = [];
    private readonly List<TEntity> _updated = [];
    private          bool          _disposed;

    protected RepositoryBase(IServiceProvider sp) {
        ServiceProvider = sp;
        AdviceContext   = new(sp);
    }

    protected virtual IServiceProvider ServiceProvider { get; }

    /// <summary><see langword="true" /> when the repository owns its context's lifetime.</summary>
    protected bool OwnsContext { get; set; } = true;

    #region IRepository<TEntity> Members

    public virtual AdviceContext AdviceContext { get; }

    public abstract IAsyncEnumerable<TResult> ListAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    );

    public virtual ValueTask<TEntity?> GetAsync(TEntity entity, CancellationToken ct = default) {
        return GetAsync<TEntity>(entity, ct);
    }

    public virtual ValueTask<TResult?> GetAsync<TResult>(TEntity entity, CancellationToken ct = default) {
        var type = entity.GetType();

        var properties = KeyPropertiesCache(type);
        if (properties.Count == 0) {
            throw new ArgumentException(SchemataResources.GetResourceString(SchemataResources.ST1020));
        }

        var keys = new List<object>();
        foreach (var property in properties) {
            var value = property.GetValue(entity);
            if (value is null) {
                throw new ArgumentException(SchemataResources.GetResourceString(SchemataResources.ST1021));
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

    public abstract ValueTask<TResult?> FirstOrDefaultAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    );

    public abstract ValueTask<TResult?> SingleOrDefaultAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    );

    public abstract ValueTask<bool> AnyAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    );

    public abstract ValueTask<int> CountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    );

    public abstract ValueTask<long> LongCountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    );

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

    /// <inheritdoc cref="IRepository{TEntity}.CommitAsync" />
    public abstract Task CommitAsync(CancellationToken ct = default);

    public virtual IDisposable SuppressAddValidation()    { return AdviceContext.Use<AddValidationSuppressed>(); }
    public virtual IDisposable SuppressUpdateValidation() { return AdviceContext.Use<UpdateValidationSuppressed>(); }
    public virtual IDisposable SuppressConcurrency()      { return AdviceContext.Use<ConcurrencySuppressed>(); }
    public virtual IDisposable SuppressQuerySoftDelete()  { return AdviceContext.Use<QuerySoftDeleteSuppressed>(); }
    public virtual IDisposable SuppressSoftDelete()       { return AdviceContext.Use<SoftDeleteSuppressed>(); }
    public virtual IDisposable SuppressTimestamp()        { return AdviceContext.Use<TimestampSuppressed>(); }

    /// <inheritdoc cref="IRepository{TEntity}.Join" />
    public virtual void Join(IUnitOfWork uow) {
        ArgumentNullException.ThrowIfNull(uow);
        if (!OwnsContext) {
            throw new InvalidOperationException("Repository is already enlisted in a unit of work.");
        }

        if (_added.Count > 0 || _updated.Count > 0 || _removed.Count > 0) {
            throw new InvalidOperationException(
                "Cannot enlist a repository with uncommitted work. "
              + "Call CommitAsync before Join, or resolve a fresh IRepository<T> instance.");
        }

        AttachContext(uow);
        OwnsContext = false;
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;

        if (OwnsContext) DisposeContext();

        _added.Clear();
        _updated.Clear();
        _removed.Clear();

        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync() {
        if (_disposed) return;
        _disposed = true;

        if (OwnsContext) await DisposeContextAsync();

        _added.Clear();
        _updated.Clear();
        _removed.Clear();

        GC.SuppressFinalize(this);
    }

    #endregion

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

    protected void TrackAdd(TEntity entity) { _added.Add(entity); }

    protected void TrackUpdate(TEntity entity) { _updated.Add(entity); }

    protected void TrackRemove(TEntity entity) { _removed.Add(entity); }

    protected abstract IQueryable<TEntity> AsQueryable();

    /// <summary>
    ///     Replaces the repository's owned context with the UoW's context. The base
    ///     <see cref="Join" /> sets <see cref="OwnsContext" /> to <see langword="false" />
    ///     after this call returns.
    /// </summary>
    protected abstract void AttachContext(IUnitOfWork uow);

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
