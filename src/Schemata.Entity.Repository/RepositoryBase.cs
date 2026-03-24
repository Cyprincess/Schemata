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
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Common;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Entity.Repository;

/// <summary>
///     Non-generic base class providing shared key-property caching logic for repository implementations.
/// </summary>
public abstract class RepositoryBase
{
    private static readonly ConcurrentDictionary<RuntimeTypeHandle, IList<PropertyInfo>> KeyProperties = [];

    /// <summary>
    ///     Returns the cached list of key properties for the specified type, discovering them by <see cref="KeyAttribute" /> or by convention (property named "Id").
    /// </summary>
    /// <param name="type">The entity type to inspect.</param>
    /// <returns>The list of key property infos.</returns>
    protected static IList<PropertyInfo> KeyPropertiesCache(Type type) {
        if (KeyProperties.TryGetValue(type.TypeHandle, out var pi)) {
            return pi;
        }

        var allProperties = TypePropertiesCache(type);
        var keyProperties = allProperties.Where(p => p.HasCustomAttribute<KeyAttribute>(true)).ToList();

        if (keyProperties.Count == 0) {
            var id = allProperties.FirstOrDefault(p => string.Equals(p.Name, "id",
                                                                     StringComparison.InvariantCultureIgnoreCase));
            if (id is not null) {
                keyProperties.Add(id);
            }
        }

        KeyProperties[type.TypeHandle] = keyProperties;
        return keyProperties;
    }

    /// <summary>
    ///     Returns the cached list of mapped (non-virtual, readable, not <see cref="NotMappedAttribute" />) properties for the specified type.
    /// </summary>
    /// <param name="type">The entity type to inspect.</param>
    /// <returns>The list of mapped property infos.</returns>
    protected static IList<PropertyInfo> TypePropertiesCache(Type type) {
        var properties = AppDomainTypeCache.GetProperties(type).Values.Where(IsNotVirtual).ToList();
        return properties;
    }

    private static bool IsNotVirtual(PropertyInfo property) {
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
///     Abstract base class for repository implementations providing advisor pipeline integration and common CRUD logic.
/// </summary>
/// <typeparam name="TEntity">The entity type managed by this repository.</typeparam>
public abstract class RepositoryBase<TEntity> : RepositoryBase, IRepository<TEntity>, IRepository
    where TEntity : class
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="RepositoryBase{TEntity}" /> class.
    /// </summary>
    /// <param name="sp">The service provider for resolving advisors and creating new instances.</param>
    protected RepositoryBase(IServiceProvider sp) {
        ServiceProvider = sp;
        AdviceContext   = new(sp);
    }

    /// <summary>
    ///     Gets the service provider used to resolve advisors and create new repository instances.
    /// </summary>
    protected virtual IServiceProvider ServiceProvider { get; }

    #region IRepository Members

    /// <inheritdoc />
    AdviceContext IRepository.AdviceContext => AdviceContext;

    /// <inheritdoc />
    async IAsyncEnumerable<object> IRepository.ListAsync<T>(
        Expression<Func<T, bool>>?                 predicate,
        [EnumeratorCancellation] CancellationToken ct
    ) {
        var query = Predicate.Cast<T, TEntity>(predicate);

        Func<IQueryable<TEntity>, IQueryable<TEntity>> expression = query is not null ? q => q.Where(query) : q => q;

        await foreach (var item in ListAsync(expression).WithCancellation(ct)) {
            ct.ThrowIfCancellationRequested();
            yield return item;
        }
    }

    /// <inheritdoc />
    async IAsyncEnumerable<object> IRepository.SearchAsync<T>(
        Expression<Func<T, bool>>?                 predicate,
        [EnumeratorCancellation] CancellationToken ct
    ) {
        var query = Predicate.Cast<T, TEntity>(predicate);

        Func<IQueryable<TEntity>, IQueryable<TEntity>> expression = query is not null ? q => q.Where(query) : q => q;

        await foreach (var item in ListAsync(expression).WithCancellation(ct)) {
            ct.ThrowIfCancellationRequested();
            yield return item;
        }
    }

    /// <inheritdoc />
    async ValueTask<object?> IRepository.FirstOrDefaultAsync<T>(
        Expression<Func<T, bool>>? predicate,
        CancellationToken          ct
    ) {
        var query = Predicate.Cast<T, TEntity>(predicate);

        Func<IQueryable<TEntity>, IQueryable<TEntity>> expression = query is not null ? q => q.Where(query) : q => q;

        return await FirstOrDefaultAsync(expression, ct);
    }

    /// <inheritdoc />
    async ValueTask<object?> IRepository.SingleOrDefaultAsync<T>(
        Expression<Func<T, bool>>? predicate,
        CancellationToken          ct
    ) {
        var query = Predicate.Cast<T, TEntity>(predicate);

        Func<IQueryable<TEntity>, IQueryable<TEntity>> expression = query is not null ? q => q.Where(query) : q => q;

        return await SingleOrDefaultAsync(expression, ct);
    }

    /// <inheritdoc />
    ValueTask<bool> IRepository.AnyAsync<T>(Expression<Func<T, bool>>? predicate, CancellationToken ct) {
        var query = Predicate.Cast<T, TEntity>(predicate);

        Func<IQueryable<TEntity>, IQueryable<TEntity>> expression = query is not null ? q => q.Where(query) : q => q;

        return AnyAsync(expression, ct);
    }

    /// <inheritdoc />
    ValueTask<int> IRepository.CountAsync<T>(Expression<Func<T, bool>>? predicate, CancellationToken ct) {
        var query = Predicate.Cast<T, TEntity>(predicate);

        Func<IQueryable<TEntity>, IQueryable<TEntity>> expression = query is not null ? q => q.Where(query) : q => q;

        return CountAsync(expression, ct);
    }

    /// <inheritdoc />
    ValueTask<long> IRepository.LongCountAsync<T>(Expression<Func<T, bool>>? predicate, CancellationToken ct) {
        var query = Predicate.Cast<T, TEntity>(predicate);

        Func<IQueryable<TEntity>, IQueryable<TEntity>> expression = query is not null ? q => q.Where(query) : q => q;

        return LongCountAsync(expression, ct);
    }

    /// <inheritdoc />
    Task IRepository.AddAsync(object entity, CancellationToken ct) {
        if (entity is not TEntity e) {
            return Task.CompletedTask;
        }

        return AddAsync(e, ct);
    }

    /// <inheritdoc />
    Task IRepository.UpdateAsync(object entity, CancellationToken ct) {
        if (entity is not TEntity e) {
            return Task.CompletedTask;
        }

        return UpdateAsync(e, ct);
    }

    /// <inheritdoc />
    Task IRepository.RemoveAsync(object entity, CancellationToken ct) {
        if (entity is not TEntity e) {
            return Task.CompletedTask;
        }

        return RemoveAsync(e, ct);
    }

    /// <inheritdoc />
    void IRepository.Detach(object entity) {
        if (entity is not TEntity e) {
            return;
        }

        Detach(e);
    }

    /// <inheritdoc />
    IRepository IRepository.Once() { return (IRepository)Once(); }

    /// <inheritdoc />
    IRepository IRepository.SuppressAddValidation() { return (IRepository)SuppressAddValidation(); }

    /// <inheritdoc />
    IRepository IRepository.SuppressUpdateValidation() { return (IRepository)SuppressUpdateValidation(); }

    /// <inheritdoc />
    IRepository IRepository.SuppressConcurrency() { return (IRepository)SuppressConcurrency(); }

    /// <inheritdoc />
    IRepository IRepository.SuppressQuerySoftDelete() { return (IRepository)SuppressQuerySoftDelete(); }

    /// <inheritdoc />
    IRepository IRepository.SuppressSoftDelete() { return (IRepository)SuppressSoftDelete(); }

    /// <inheritdoc />
    IRepository IRepository.SuppressTimestamp() { return (IRepository)SuppressTimestamp(); }

    #endregion

    #region IRepository<TEntity> Members

    /// <inheritdoc />
    public virtual AdviceContext AdviceContext { get; }

    /// <inheritdoc />
    public abstract IAsyncEnumerable<TEntity> AsAsyncEnumerable();

    /// <inheritdoc />
    public abstract IQueryable<TEntity> AsQueryable();

    /// <inheritdoc />
    public abstract IAsyncEnumerable<TResult> ListAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    );

    /// <inheritdoc />
    public abstract IAsyncEnumerable<TResult> SearchAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    );

    /// <inheritdoc />
    public virtual ValueTask<TEntity?> GetAsync(TEntity entity, CancellationToken ct = default) {
        return GetAsync<TEntity>(entity, ct);
    }

    /// <inheritdoc />
    public virtual ValueTask<TResult?> GetAsync<TResult>(TEntity entity, CancellationToken ct = default) {
        var type = entity.GetType();

        var properties = KeyPropertiesCache(type);
        if (properties.Count == 0) {
            throw new ArgumentException("Entity must have at least one [Key]");
        }

        var keys = new List<object>();
        foreach (var property in properties) {
            var value = property.GetValue(entity);
            if (value is null) {
                throw new ArgumentException("Entity key cannot be null");
            }

            keys.Add(value);
        }

        return FindAsync<TResult>(keys.ToArray(), ct);
    }

    /// <inheritdoc />
    public virtual ValueTask<TEntity?> FindAsync(object[] keys, CancellationToken ct = default) {
        return FindAsync<TEntity>(keys, ct);
    }

    /// <inheritdoc />
    public virtual async ValueTask<TResult?> FindAsync<TResult>(object[] keys, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        var type = typeof(TEntity);

        var properties = KeyPropertiesCache(type);
        if (properties.Count == 0) {
            throw new ArgumentException("Entity must have at least one [Key]");
        }

        if (properties.Count != keys.Length) {
            throw new ArgumentException("Entity key count mismatch");
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

    /// <inheritdoc />
    public abstract ValueTask<TResult?> FirstOrDefaultAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    );

    /// <inheritdoc />
    public abstract ValueTask<TResult?> SingleOrDefaultAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    );

    /// <inheritdoc />
    public abstract ValueTask<bool> AnyAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    );

    /// <inheritdoc />
    public abstract ValueTask<int> CountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    );

    /// <inheritdoc />
    public abstract ValueTask<long> LongCountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    );

    /// <inheritdoc />
    public abstract Task AddAsync(TEntity entity, CancellationToken ct = default);

    /// <inheritdoc />
    public virtual Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default) {
        var tasks = entities.Select(e => AddAsync(e, ct)).ToArray();

        return Task.WhenAll(tasks);
    }

    /// <inheritdoc />
    public abstract Task UpdateAsync(TEntity entity, CancellationToken ct = default);

    /// <inheritdoc />
    public abstract Task RemoveAsync(TEntity entity, CancellationToken ct = default);

    /// <inheritdoc />
    public virtual Task RemoveRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default) {
        var tasks = entities.Select(e => RemoveAsync(e, ct)).ToArray();

        return Task.WhenAll(tasks);
    }

    /// <inheritdoc />
    public abstract ValueTask<int> CommitAsync(CancellationToken ct = default);

    /// <inheritdoc />
    public abstract void Detach(TEntity entity);

    /// <inheritdoc />
    public virtual IRepository<TEntity> Once() {
        var type = GetType();
        return (IRepository<TEntity>)ActivatorUtilities.CreateInstance(ServiceProvider, type);
    }

    /// <inheritdoc />
    public virtual IRepository<TEntity> SuppressAddValidation() {
        AdviceContext.Set<SuppressAddValidation>(null);
        return this;
    }

    /// <inheritdoc />
    public virtual IRepository<TEntity> SuppressUpdateValidation() {
        AdviceContext.Set<SuppressUpdateValidation>(null);
        return this;
    }

    /// <inheritdoc />
    public virtual IRepository<TEntity> SuppressConcurrency() {
        AdviceContext.Set<SuppressConcurrency>(null);
        return this;
    }

    /// <inheritdoc />
    public virtual IRepository<TEntity> SuppressQuerySoftDelete() {
        AdviceContext.Set<SuppressQuerySoftDelete>(null);
        return this;
    }

    /// <inheritdoc />
    public virtual IRepository<TEntity> SuppressSoftDelete() {
        AdviceContext.Set<SuppressSoftDelete>(null);
        return this;
    }

    /// <inheritdoc />
    public virtual IRepository<TEntity> SuppressTimestamp() {
        AdviceContext.Set<SuppressTimestamp>(null);
        return this;
    }

    #endregion

    /// <summary>
    ///     Creates a <see cref="QueryContainer{TEntity}" /> from the current queryable, for use by the build-query advisor pipeline.
    /// </summary>
    /// <returns>A new query container wrapping this repository and its queryable.</returns>
    protected virtual QueryContainer<TEntity> AsQueryContainer() {
        var query = AsQueryable();

        var container = new QueryContainer<TEntity>(this, query);

        return container;
    }

    /// <summary>
    ///     Applies the user-supplied predicate to the query, or falls back to <see cref="Queryable.OfType{TResult}" />.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="query">The base queryable after build-query advisors have run.</param>
    /// <param name="predicate">An optional query transformation.</param>
    /// <returns>The final projected queryable.</returns>
    protected virtual IQueryable<TResult> BuildQuery<TResult>(
        IQueryable<TEntity>                             query,
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate
    ) {
        if (predicate is not null) {
            return predicate(query);
        }

        return query.OfType<TResult>();
    }
}
