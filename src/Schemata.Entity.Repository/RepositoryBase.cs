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
using Microsoft.EntityFrameworkCore;
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
    ///     Resolves the key properties for the specified type by reading the class-level
    ///     <see cref="PrimaryKeyAttribute" /> (EF Core 7+), then falling back to the
    ///     <see cref="IIdentifier.Uid" /> convention when no attribute is declared.
    /// </summary>
    /// <param name="type">The entity type to inspect.</param>
    /// <returns>The list of key property infos.</returns>
    public static IReadOnlyList<PropertyInfo> ResolveKeyProperties(Type type) {
        var properties = TypePropertiesCache(type);

        var classKey = type.GetCustomAttribute<PrimaryKeyAttribute>(true);
        if (classKey is not null) {
            var byName   = properties.ToDictionary(p => p.Name, StringComparer.Ordinal);
            var resolved = new List<PropertyInfo>();
            foreach (var name in classKey.PropertyNames) {
                if (byName.TryGetValue(name, out var prop)) {
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
public abstract class RepositoryBase<TEntity> : RepositoryBase, IRepository<TEntity>, IRepository
    where TEntity : class
{
    private readonly List<Func<CancellationToken, Task>> _afterCommit = [];

    protected RepositoryBase(IServiceProvider sp, IUnitOfWork? uow = null) {
        ServiceProvider = sp;
        UnitOfWork      = uow;
        AdviceContext   = new(sp);
    }

    protected virtual IServiceProvider ServiceProvider { get; }

    protected virtual IUnitOfWork? UnitOfWork { get; }

    protected async Task DrainAfterCommitAsync(CancellationToken ct) {
        if (_afterCommit.Count == 0) {
            return;
        }

        var pending = _afterCommit.ToArray();
        _afterCommit.Clear();

        List<Exception>? errors = null;
        foreach (var action in pending) {
            try {
                await action(ct);
            } catch (Exception ex) {
                (errors ??= []).Add(ex);
            }
        }

        if (errors is not null) {
            throw errors.Count == 1 ? errors[0] : new AggregateException(errors);
        }
    }

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

        await foreach (var item in SearchAsync(expression).WithCancellation(ct)) {
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

    Task IRepository.AddAsync(object entity, CancellationToken ct) { return AddAsync(Cast(entity), ct); }

    Task IRepository.UpdateAsync(object entity, CancellationToken ct) { return UpdateAsync(Cast(entity), ct); }

    Task IRepository.RemoveAsync(object entity, CancellationToken ct) { return RemoveAsync(Cast(entity), ct); }

    void IRepository.Detach(object entity) { Detach(Cast(entity)); }

    void IRepository.EnqueueAfterCommit(Func<CancellationToken, Task> action) { EnqueueAfterCommit(action); }

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
    public abstract ValueTask<int> CommitAsync(CancellationToken ct = default);

    /// <inheritdoc cref="IRepository{TEntity}.EnqueueAfterCommit" />
    public virtual void EnqueueAfterCommit(Func<CancellationToken, Task> action) {
        if (action is null) {
            throw new ArgumentNullException(nameof(action));
        }

        if (UnitOfWork is { IsActive: true }) {
            UnitOfWork.EnqueueAfterCommit(action);
            return;
        }

        _afterCommit.Add(action);
    }

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
                "IUnitOfWork not registered. Call `.WithUnitOfWork<TContext>()` during configuration.");
        }

        UnitOfWork.Begin();
        return UnitOfWork;
    }

    #endregion

    private static TEntity Cast(object entity) {
        if (entity is not TEntity typed) {
            throw new ArgumentException(
                $"Entity of type '{entity.GetType()}' cannot be operated on by IRepository<{typeof(TEntity)}>.",
                nameof(entity));
        }

        return typed;
    }

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
}
