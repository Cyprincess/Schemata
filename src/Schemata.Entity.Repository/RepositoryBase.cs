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
using Schemata.Abstractions;
using Schemata.Abstractions.Advices;
using Schemata.Entity.Repository.Advices;

namespace Schemata.Entity.Repository;

public abstract class RepositoryBase
{
    private static readonly ConcurrentDictionary<RuntimeTypeHandle, IList<PropertyInfo>> KeyProperties = [];

    protected static IList<PropertyInfo> KeyPropertiesCache(Type type) {
        if (KeyProperties.TryGetValue(type.TypeHandle, out var pi)) {
            return pi;
        }

        var allProperties = TypePropertiesCache(type);
        var keyProperties = allProperties.Where(p => p.HasCustomAttribute<KeyAttribute>(true)).ToList();

        if (keyProperties.Count == 0) {
            var id = allProperties.FirstOrDefault(p
                => string.Equals(p.Name, "id", StringComparison.InvariantCultureIgnoreCase));
            if (id is not null) {
                keyProperties.Add(id);
            }
        }

        KeyProperties[type.TypeHandle] = keyProperties;
        return keyProperties;
    }

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

public abstract class RepositoryBase<TEntity> : RepositoryBase, IRepository<TEntity>, IRepository
    where TEntity : class
{
    public RepositoryBase(IServiceProvider sp) {
        ServiceProvider = sp;
    }

    protected virtual IServiceProvider ServiceProvider { get; }

    #region IRepository Members

    AdviceContext IRepository.AdviceContext => AdviceContext;

    async IAsyncEnumerable<object> IRepository.ListAsync<T>(
        Expression<Func<T, bool>>?                 predicate,
        [EnumeratorCancellation] CancellationToken ct) {
        var query = Predicate.Cast<T, TEntity>(predicate);

        Func<IQueryable<TEntity>, IQueryable<TEntity>> expression = query is not null ? q => q.Where(query) : q => q;

        await foreach (var item in ListAsync(expression, ct)) {
            ct.ThrowIfCancellationRequested();
            yield return item;
        }
    }

    async IAsyncEnumerable<object> IRepository.SearchAsync<T>(
        Expression<Func<T, bool>>?                 predicate,
        [EnumeratorCancellation] CancellationToken ct) {
        var query = Predicate.Cast<T, TEntity>(predicate);

        Func<IQueryable<TEntity>, IQueryable<TEntity>> expression = query is not null ? q => q.Where(query) : q => q;

        await foreach (var item in ListAsync(expression, ct)) {
            ct.ThrowIfCancellationRequested();
            yield return item;
        }
    }

    async ValueTask<object?> IRepository.FirstOrDefaultAsync<T>(
        Expression<Func<T, bool>>? predicate,
        CancellationToken          ct) {
        var query = Predicate.Cast<T, TEntity>(predicate);

        Func<IQueryable<TEntity>, IQueryable<TEntity>> expression = query is not null ? q => q.Where(query) : q => q;

        return await FirstOrDefaultAsync(expression, ct);
    }

    async ValueTask<object?> IRepository.SingleOrDefaultAsync<T>(
        Expression<Func<T, bool>>? predicate,
        CancellationToken          ct) {
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

    IRepository IRepository.Once() {
        return (IRepository)Once();
    }

    IRepository IRepository.SuppressAddValidation() {
        return (IRepository)SuppressAddValidation();
    }

    IRepository IRepository.SuppressUpdateValidation() {
        return (IRepository)SuppressUpdateValidation();
    }

    IRepository IRepository.SuppressUpdateConcurrency() {
        return (IRepository)SuppressUpdateConcurrency();
    }

    IRepository IRepository.SuppressQuerySoftDelete() {
        return (IRepository)SuppressQuerySoftDelete();
    }

    IRepository IRepository.SuppressRemoveSoftDelete() {
        return (IRepository)SuppressRemoveSoftDelete();
    }

    #endregion

    #region IRepository<TEntity> Members

    public virtual AdviceContext AdviceContext { get; } = new();

    public abstract IAsyncEnumerable<TEntity> AsAsyncEnumerable();

    public abstract IQueryable<TEntity> AsQueryable();

    public abstract string? GetQueryString<T>(IQueryable<T> query);

    public abstract IAsyncEnumerable<TResult> ListAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default);

    public abstract IAsyncEnumerable<TResult> SearchAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default);

    public virtual ValueTask<TEntity?> GetAsync(TEntity entity, CancellationToken ct = default) {
        return GetAsync<TEntity>(entity, ct);
    }

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

    public virtual ValueTask<TEntity?> FindAsync(object[] keys, CancellationToken ct = default) {
        return FindAsync<TEntity>(keys, ct);
    }

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

    public abstract ValueTask<TResult?> FirstOrDefaultAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default);

    public abstract ValueTask<TResult?> SingleOrDefaultAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default);

    public abstract ValueTask<bool> AnyAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default);

    public abstract ValueTask<int> CountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default);

    public abstract ValueTask<long> LongCountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default);

    public abstract Task AddAsync(TEntity entity, CancellationToken ct = default);

    public virtual Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default) {
        var tasks = entities.Select(e => AddAsync(e, ct)).ToArray();

        return Task.WhenAny(tasks);
    }

    public abstract Task UpdateAsync(TEntity entity, CancellationToken ct = default);

    public abstract Task RemoveAsync(TEntity entity, CancellationToken ct = default);

    public virtual Task RemoveRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default) {
        var tasks = entities.Select(e => RemoveAsync(e, ct)).ToArray();

        return Task.WhenAny(tasks);
    }

    public abstract ValueTask<int> CommitAsync(CancellationToken ct = default);

    public virtual IRepository<TEntity> Once() {
        var type = GetType();
        return (IRepository<TEntity>)ActivatorUtilities.CreateInstance(ServiceProvider, type);
    }

    public virtual IRepository<TEntity> SuppressAddValidation() {
        AdviceContext.Set<SuppressAddValidation>(default);
        return this;
    }

    public virtual IRepository<TEntity> SuppressUpdateValidation() {
        AdviceContext.Set<SuppressUpdateValidation>(default);
        return this;
    }

    public virtual IRepository<TEntity> SuppressUpdateConcurrency() {
        AdviceContext.Set<SuppressUpdateConcurrency>(default);
        return this;
    }

    public virtual IRepository<TEntity> SuppressQuerySoftDelete() {
        AdviceContext.Set<SuppressQuerySoftDelete>(default);
        return this;
    }

    public virtual IRepository<TEntity> SuppressRemoveSoftDelete() {
        AdviceContext.Set<SuppressRemoveSoftDelete>(default);
        return this;
    }

    #endregion

    protected virtual IQueryable<TResult> BuildQuery<TResult>(
        IQueryable<TEntity>                             table,
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate) {
        if (predicate is not null) {
            return predicate(table);
        }

        return table.OfType<TResult>();
    }
}
