using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Microsoft.Extensions.Caching.Memory;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Skeleton.Services;

public class SchemataTenantManager(IMemoryCache cache, IRepository<SchemataTenant<Guid>> tenants) : SchemataTenantManager<SchemataTenant<Guid>, Guid>(cache, tenants), ITenantManager;

public class SchemataTenantManager<TTenant, TKey>(IMemoryCache cache, IRepository<TTenant> tenants) : ITenantManager<TTenant, TKey>
    where TTenant : SchemataTenant<TKey>
    where TKey : struct, IEquatable<TKey>
{
    #region ITenantManager<TTenant,TKey> Members

    public virtual ValueTask<TTenant?> FindByIdAsync(long id, CancellationToken ct) {
        return tenants.SingleOrDefaultAsync(q => q.Where(t => t.Id == id), ct);
    }

    public virtual ValueTask<TTenant?> FindByTenantId(TKey identifier, CancellationToken ct) {
        return tenants.SingleOrDefaultAsync(q => q.Where(t => t.TenantId!.Equals(identifier)), ct);
    }

    public ValueTask<TTenant?> FindByHost(string host, CancellationToken ct) {
        var wrapped = $"\"{host}\"";
        return tenants.SingleOrDefaultAsync(q => q.Where(t => t.Hosts!.Contains(wrapped)), ct);
    }

    public virtual ValueTask<ImmutableArray<string>> GetHostsAsync(TTenant tenant, CancellationToken ct) {
        if (string.IsNullOrEmpty(tenant.Hosts)) {
            return new(ImmutableArray<string>.Empty);
        }

        var key = tenant.Hosts!.ToCacheKey();
        var hosts = cache.GetOrCreate(key, entry => {
            entry.SetPriority(CacheItemPriority.High).SetSlidingExpiration(TimeSpan.FromMinutes(1));

            var result = JsonSerializer.Deserialize<ImmutableArray<string>?>(tenant.Hosts!);

            return result ?? ImmutableArray<string>.Empty;
        })!;

        return new(hosts);
    }

    public virtual ValueTask SetTenantId(TTenant tenant, TKey? identifier, CancellationToken ct) {
        tenant.TenantId = identifier;
        return default;
    }

    public virtual ValueTask SetDisplayNameAsync(TTenant tenant, string? name, CancellationToken ct) {
        tenant.DisplayName = name;
        return default;
    }

    public virtual ValueTask SetDisplayNamesAsync(
        TTenant                                  tenant,
        ImmutableDictionary<CultureInfo, string> names,
        CancellationToken                        ct) {
        if (names is not { Count: > 0 }) {
            tenant.DisplayNames = null;
            return default;
        }

        var dictionary = names.ToDictionary(kv => kv.Key.Name, kv => kv.Value);
        tenant.DisplayNames = JsonSerializer.Serialize(dictionary);
        return default;
    }

    public virtual ValueTask SetHostsAsync(TTenant tenant, ImmutableArray<string> hosts, CancellationToken ct) {
        if (hosts.IsDefaultOrEmpty) {
            tenant.Hosts = null;
            return default;
        }

        tenant.Hosts = JsonSerializer.Serialize(hosts);
        return default;
    }

    public virtual async ValueTask CreateAsync(TTenant tenant, CancellationToken ct) {
        await tenants.AddAsync(tenant, ct);
        await tenants.CommitAsync(ct);
    }

    public virtual async ValueTask DeleteAsync(TTenant tenant, CancellationToken ct) {
        await tenants.RemoveAsync(tenant, ct);
        await tenants.CommitAsync(ct);
    }

    public virtual async ValueTask UpdateAsync(TTenant tenant, CancellationToken ct) {
        await tenants.UpdateAsync(tenant, ct);
        await tenants.CommitAsync(ct);
    }

    #endregion
}
