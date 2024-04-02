using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Entity.Repository;

public abstract class RepositoryBase
{
    private static readonly ConcurrentDictionary<RuntimeTypeHandle, IList<PropertyInfo>> KeyProperties  = new();
    private static readonly ConcurrentDictionary<RuntimeTypeHandle, IList<PropertyInfo>> TypeProperties = new();

    protected static IList<PropertyInfo> KeyPropertiesCache(Type type) {
        if (KeyProperties.TryGetValue(type.TypeHandle, out var pi)) return pi;

        var allProperties = TypePropertiesCache(type);
        var keyProperties = allProperties.Where(p => p.HasCustomAttribute<KeyAttribute>(true)).ToList();

        if (keyProperties.Count == 0) {
            var id = allProperties.FirstOrDefault(p => string.Equals(p.Name, "id", StringComparison.InvariantCultureIgnoreCase));
            if (id != null) {
                keyProperties.Add(id);
            }
        }

        KeyProperties[type.TypeHandle] = keyProperties;
        return keyProperties;
    }

    protected static IList<PropertyInfo> TypePropertiesCache(Type type) {
        if (TypeProperties.TryGetValue(type.TypeHandle, out var pis)) return pis;

        var properties = type.GetProperties().Where(IsNotVirtual).ToList();
        TypeProperties[type.TypeHandle] = properties;
        return properties;
    }

    private static bool IsNotVirtual(PropertyInfo property) {
        if (!property.CanRead) return false;

        var getter = property.GetGetMethod();
        if (getter == null) return false;

        return !property.HasCustomAttribute<NotMappedAttribute>(false);
    }
}

public abstract class RepositoryBase<TEntity> : RepositoryBase, IRepository<TEntity>
    where TEntity : class
{
    #region IRepository<TEntity> Members

    public abstract IAsyncEnumerable<TEntity> AsAsyncEnumerable();

    public abstract IQueryable<TEntity> AsQueryable();

    public abstract IAsyncEnumerable<TResult> ListAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default);

    public virtual ValueTask<TEntity?> GetAsync(TEntity entity, CancellationToken ct = default) {
        var type = typeof(TEntity);

        var properties = KeyPropertiesCache(type);
        if (properties.Count == 0) {
            throw new ArgumentException("Entity must have at least one [Key]");
        }

        var keys = new List<object>();
        foreach (var property in properties) {
            var value = property.GetValue(entity);
            if (value == null) {
                throw new ArgumentException("Entity key cannot be null");
            }

            keys.Add(value);
        }

        return FindAsync(keys.ToArray(), ct);
    }

    public abstract ValueTask<TEntity?> FindAsync(object[] keys, CancellationToken ct = default);

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

    #endregion

    protected IQueryable<TResult> BuildQuery<TResult>(
        IQueryable<TEntity>                             table,
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate) {
        if (predicate != null) {
            return predicate(table);
        }

        if (typeof(TResult) == typeof(TEntity)) {
            return (IQueryable<TResult>)table;
        }

        return table.Select(e => (TResult)(object)e);
    }
}
