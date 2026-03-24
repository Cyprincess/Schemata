using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Schemata.Entity.Repository;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Skeleton.Services;

/// <summary>
///     Default tenant manager using <see cref="SchemataTenant{TKey}" /> with <see cref="Guid" /> keys.
/// </summary>
public class SchemataTenantManager : SchemataTenantManager<SchemataTenant<Guid>, Guid>, ITenantManager
{
    /// <inheritdoc />
    public SchemataTenantManager(IMemoryCache cache, IRepository<SchemataTenant<Guid>> tenants) :
        base(cache, tenants) { }
}

/// <summary>
///     Repository-backed implementation of <see cref="ITenantManager{TTenant, TKey}" />.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
/// <typeparam name="TKey">The tenant identifier type.</typeparam>
public class SchemataTenantManager<TTenant, TKey> : ITenantManager<TTenant, TKey>
    where TTenant : SchemataTenant<TKey>
    where TKey : struct, IEquatable<TKey>
{
    private readonly IMemoryCache         _cache;
    private readonly IRepository<TTenant> _tenants;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SchemataTenantManager{TTenant, TKey}" /> class.
    /// </summary>
    public SchemataTenantManager(IMemoryCache cache, IRepository<TTenant> tenants) {
        _cache   = cache;
        _tenants = tenants;
    }

    #region ITenantManager<TTenant,TKey> Members

    /// <inheritdoc />
    public virtual ValueTask<TTenant?> FindByIdAsync(long id, CancellationToken ct) {
        return _tenants.SingleOrDefaultAsync(q => q.Where(t => t.Id == id), ct);
    }

    /// <inheritdoc />
    public virtual ValueTask<TTenant?> FindByTenantId(TKey identifier, CancellationToken ct) {
        return _tenants.SingleOrDefaultAsync(q => q.Where(t => t.TenantId!.Equals(identifier)), ct);
    }

    /// <inheritdoc />
    public ValueTask<TTenant?> FindByHost(string host, CancellationToken ct) {
        var wrapped = $"\"{host}\"";
        return _tenants.SingleOrDefaultAsync(q => q.Where(t => t.Hosts!.Contains(wrapped)), ct);
    }

    /// <inheritdoc />
    public virtual ValueTask<ImmutableArray<string>> GetHostsAsync(TTenant tenant, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(tenant.Hosts)) {
            return new(ImmutableArray<string>.Empty);
        }

        var key = tenant.Hosts!.ToCacheKey();
        var hosts = _cache.GetOrCreate(key, entry => {
            entry.SetPriority(CacheItemPriority.High).SetSlidingExpiration(TimeSpan.FromMinutes(1));

            var result = JsonSerializer.Deserialize<ImmutableArray<string>?>(tenant.Hosts!);

            return result ?? ImmutableArray<string>.Empty;
        })!;

        return new(hosts);
    }

    /// <inheritdoc />
    public virtual ValueTask SetTenantId(TTenant tenant, TKey? identifier, CancellationToken ct) {
        tenant.TenantId = identifier;
        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetDisplayNameAsync(TTenant tenant, string? name, CancellationToken ct) {
        tenant.DisplayName = name;
        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetDisplayNamesAsync(
        TTenant                                  tenant,
        ImmutableDictionary<CultureInfo, string> names,
        CancellationToken                        ct
    ) {
        if (names is not { Count: > 0 }) {
            tenant.DisplayNames = null;
            return default;
        }

        var dictionary = names.ToDictionary(kv => kv.Key.Name, kv => kv.Value);
        tenant.DisplayNames = JsonSerializer.Serialize(dictionary);

        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetHostsAsync(TTenant tenant, ImmutableArray<string> hosts, CancellationToken ct) {
        if (hosts.IsDefaultOrEmpty) {
            tenant.Hosts = null;
            return default;
        }

        tenant.Hosts = JsonSerializer.Serialize(hosts);

        return default;
    }

    /// <inheritdoc />
    public virtual async ValueTask CreateAsync(TTenant tenant, CancellationToken ct) {
        await _tenants.AddAsync(tenant, ct);
        await _tenants.CommitAsync(ct);
    }

    /// <inheritdoc />
    public virtual async ValueTask DeleteAsync(TTenant tenant, CancellationToken ct) {
        await _tenants.RemoveAsync(tenant, ct);
        await _tenants.CommitAsync(ct);
    }

    /// <inheritdoc />
    public virtual async ValueTask UpdateAsync(TTenant tenant, CancellationToken ct) {
        await _tenants.UpdateAsync(tenant, ct);
        await _tenants.CommitAsync(ct);
    }

    #endregion
}
