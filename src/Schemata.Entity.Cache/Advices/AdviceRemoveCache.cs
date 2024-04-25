using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Schemata.Abstractions;
using Schemata.Abstractions.Advices;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advices;

namespace Schemata.Entity.Cache.Advices;

public sealed class AdviceRemoveCache<TEntity> : IRepositoryUpdateAsyncAdvice<TEntity>,
                                                 IRepositoryRemoveAsyncAdvice<TEntity>
    where TEntity : class
{
    private readonly IMemoryCache _cache;

    public AdviceRemoveCache(IMemoryCache cache) {
        _cache = cache;
    }

    #region IRepositoryUpdateAsyncAdvice<TEntity> Members

    public int Order => 1_000;

    public int Priority => Order;

    public Task<bool> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct) {
        if (ctx.Has<SuppressQueryCache>()) {
            return Task.FromResult(true);
        }

        var type = typeof(TEntity);
        var name = $"{type.FullName ?? type.Name}.";

        foreach (var g in AdviceResultCache.AffectedKeys.Keys.Where(k => k.StartsWith(name))
                                           .GroupBy(k => k.Split('=').First())) {
            var kv       = "";
            var property = g.Key.Substring(name.Length);
            if (property.StartsWith("\x1e")) {
                kv = g.Key;
            } else {
                var info = AppDomainTypeCache.GetProperty(type, property);
                if (info is null) {
                    continue;
                }

                var value = info.GetValue(entity);
                kv = $"{name}{property}={value}";
            }

            if (!AdviceResultCache.AffectedKeys.TryGetValue(kv, out var key)) {
                continue;
            }

            AdviceResultCache.AffectedKeys.TryRemove(kv, out var _);
            _cache.Remove(key);
        }

        return Task.FromResult(true);
    }

    #endregion
}
