using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
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
    ///     Resolves the key properties for the specified type by scanning
    ///     <see cref="TableKeyAttribute" /> or falling back to the "Id" convention.
    /// </summary>
    /// <param name="type">The entity type to inspect.</param>
    /// <returns>The list of key property infos.</returns>
    public static IReadOnlyList<PropertyInfo> ResolveKeyProperties(Type type) {
        var properties = TypePropertiesCache(type);

        var keys = properties
                  .SelectMany(p => p.GetCustomAttributes<TableKeyAttribute>(true)
                                    .Select(a => (Property: p, Order: a.Order)))
                  .OrderBy(x => x.Order)
                  .Select(x => x.Property)
                  .ToList();

        if (keys.Count > 0) {
            return keys;
        }

        var id = properties.FirstOrDefault(p => string.Equals(
                                               p.Name,
                                               nameof(IIdentifier.Uid),
                                               StringComparison.InvariantCultureIgnoreCase
                                           )
        );
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

    /// <summary>
    ///     Returns the cached list of mapped (non-virtual, readable, not
    ///     <see cref="NotMappedAttribute" />) properties for the specified type.
    /// </summary>
    /// <param name="type">The entity type to inspect.</param>
    /// <returns>The list of mapped property infos.</returns>
    protected static IReadOnlyList<PropertyInfo> TypePropertiesCache(Type type) {
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
///     Abstract base class for repository implementations providing advisor pipeline
///     integration and common CRUD logic. All query methods pass through
///     <see cref="IRepositoryBuildQueryAdvisor{TEntity}" />; mutate methods delegate
///     to abstract/virtual methods that concrete providers must implement.
/// </summary>
/// <typeparam name="TEntity">The entity type managed by this repository.</typeparam>
public abstract class RepositoryBase<TEntity> : RepositoryBase, IRepository<TEntity>, IRepository
    where TEntity : class
{
    /// <summary>
    ///     Initializes a new instance of <see cref="RepositoryBase{TEntity}" />.
    /// </summary>
    /// <param name="sp">
    ///     The service provider for resolving advisors and creating new instances via
    ///     <see cref="Once" />.
    /// </param>
    /// <param name="uow">An optional unit of work for coordinating cross-repository transactions.</param>
    protected RepositoryBase(IServiceProvider sp, IUnitOfWork? uow = null) {
        ServiceProvider = sp;
        UnitOfWork      = uow;
        AdviceContext   = new(sp);
    }

    /// <summary>
    ///     Gets the service provider used to resolve advisors and create new repository
    ///     instances.
    /// </summary>
    protected virtual IServiceProvider ServiceProvider { get; }

    /// <summary>
    ///     Gets the unit of work for coordinating cross-repository transactions, if one was provided.
    /// </summary>
    protected virtual IUnitOfWork? UnitOfWork { get; }

    #region IRepository Members

    AdviceContext IRepository.AdviceContext => AdviceContext;

    IUnitOfWork IRepository.BeginWork() { return BeginWork(); }

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

    async ValueTask<object?> IRepository.FirstOrDefaultAsync<T>(
        Expression<Func<T, bool>>? predicate,
        CancellationToken          ct
    ) {
        var query = Predicate.Cast<T, TEntity>(predicate);

        Func<IQueryable<TEntity>, IQueryable<TEntity>> expression = query is not null ? q => q.Where(query) : q => q;

        return await FirstOrDefaultAsync(expression, ct);
    }

    async ValueTask<object?> IRepository.SingleOrDefaultAsync<T>(
        Expression<Func<T, bool>>? predicate,
        CancellationToken          ct
    ) {
        var query = Predicate.Cast<T, TEntity>(predicate);

        Func<IQueryable<TEntity>, IQueryable<TEntity>> expression = query is not null ? q => q.Where(query) : q => q;

        return await SingleOrDefaultAsync(expression, ct);
    }

    ValueTask<bool> IRepository.AnyAsync<T>(Expression<Func<T, bool>>? predicate, CancellationToken ct) {
        var query = Predicate.Cast<T, TEntity>(predicate);

        Func<IQueryable<TEntity>, IQueryable<TEntity>> expression = query is not null ? q => q.Where(query) : q => q;

        return AnyAsync(expression, ct);
    }

    ValueTask<int> IRepository.CountAsync<T>(Expression<Func<T, bool>>? predicate, CancellationToken ct) {
        var query = Predicate.Cast<T, TEntity>(predicate);

        Func<IQueryable<TEntity>, IQueryable<TEntity>> expression = query is not null ? q => q.Where(query) : q => q;

        return CountAsync(expression, ct);
    }

    ValueTask<long> IRepository.LongCountAsync<T>(Expression<Func<T, bool>>? predicate, CancellationToken ct) {
        var query = Predicate.Cast<T, TEntity>(predicate);

        Func<IQueryable<TEntity>, IQueryable<TEntity>> expression = query is not null ? q => q.Where(query) : q => q;

        return LongCountAsync(expression, ct);
    }

    Task IRepository.AddAsync(object entity, CancellationToken ct) {
        if (entity is not TEntity e) {
            return Task.CompletedTask;
        }

        return AddAsync(e, ct);
    }

    Task IRepository.UpdateAsync(object entity, CancellationToken ct) {
        if (entity is not TEntity e) {
            return Task.CompletedTask;
        }

        return UpdateAsync(e, ct);
    }

    Task IRepository.RemoveAsync(object entity, CancellationToken ct) {
        if (entity is not TEntity e) {
            return Task.CompletedTask;
        }

        return RemoveAsync(e, ct);
    }

    void IRepository.Detach(object entity) {
        if (entity is not TEntity e) {
            return;
        }

        Detach(e);
    }

    IRepository IRepository.Once() { return (IRepository)Once(); }

    IRepository IRepository.SuppressAddValidation() { return (IRepository)SuppressAddValidation(); }

    IRepository IRepository.SuppressUpdateValidation() { return (IRepository)SuppressUpdateValidation(); }

    IRepository IRepository.SuppressConcurrency() { return (IRepository)SuppressConcurrency(); }

    IRepository IRepository.SuppressQuerySoftDelete() { return (IRepository)SuppressQuerySoftDelete(); }

    IRepository IRepository.SuppressSoftDelete() { return (IRepository)SuppressSoftDelete(); }

    IRepository IRepository.SuppressTimestamp() { return (IRepository)SuppressTimestamp(); }

    #endregion

    #region IRepository<TEntity> Members

    public virtual AdviceContext AdviceContext { get; }

    public abstract IAsyncEnumerable<TEntity> AsAsyncEnumerable();

    public abstract IQueryable<TEntity> AsQueryable();

    public abstract IAsyncEnumerable<TResult> ListAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    );

    public abstract IAsyncEnumerable<TResult> SearchAsync<TResult>(
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

    public virtual Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default) {
        var tasks = entities.Select(e => AddAsync(e, ct)).ToArray();

        return Task.WhenAll(tasks);
    }

    public abstract Task UpdateAsync(TEntity entity, CancellationToken ct = default);

    public abstract Task RemoveAsync(TEntity entity, CancellationToken ct = default);

    public virtual Task RemoveRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default) {
        var tasks = entities.Select(e => RemoveAsync(e, ct)).ToArray();

        return Task.WhenAll(tasks);
    }

    /// <inheritdoc cref="IRepository{TEntity}.CommitAsync" />
    public abstract ValueTask<int> CommitAsync(CancellationToken ct = default);

    public abstract void Detach(TEntity entity);

    public virtual IRepository<TEntity> Once() {
        var type = GetType();
        return (IRepository<TEntity>)ActivatorUtilities.CreateInstance(ServiceProvider, type);
    }

    public virtual IRepository<TEntity> SuppressAddValidation() {
        AdviceContext.Set<AddValidationSuppressed>(null);
        return this;
    }

    public virtual IRepository<TEntity> SuppressUpdateValidation() {
        AdviceContext.Set<UpdateValidationSuppressed>(null);
        return this;
    }

    public virtual IRepository<TEntity> SuppressConcurrency() {
        AdviceContext.Set<ConcurrencySuppressed>(null);
        return this;
    }

    public virtual IRepository<TEntity> SuppressQuerySoftDelete() {
        AdviceContext.Set<QuerySoftDeleteSuppressed>(null);
        return this;
    }

    public virtual IRepository<TEntity> SuppressSoftDelete() {
        AdviceContext.Set<SoftDeleteSuppressed>(null);
        return this;
    }

    public virtual IRepository<TEntity> SuppressTimestamp() {
        AdviceContext.Set<TimestampSuppressed>(null);
        return this;
    }

    public virtual IUnitOfWork BeginWork() {
        if (UnitOfWork is null) {
            throw new InvalidOperationException(
                "IUnitOfWork not registered. Call `.WithUnitOfWork<TContext>()` during configuration."
            );
        }

        UnitOfWork.Begin();
        return UnitOfWork;
    }

    #endregion

    /// <summary>
    ///     Creates a <see cref="QueryContainer{TEntity}" /> from the current queryable
    ///     for use by the build-query advisor pipeline
    ///     (see <see cref="IRepositoryBuildQueryAdvisor{TEntity}" />).
    /// </summary>
    /// <returns>A new query container wrapping this repository and its queryable.</returns>
    protected virtual QueryContainer<TEntity> AsQueryContainer() {
        var query = AsQueryable();

        var container = new QueryContainer<TEntity>(this, query);

        return container;
    }

    /// <summary>
    ///     Applies the user-supplied predicate to the advisor-processed query, or falls
    ///     back to <see cref="Queryable.OfType{TResult}" /> when no predicate is provided.
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
