using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Skeleton.Services;

/// <summary>
///     Default tenant manager using <see cref="SchemataTenant{TKey}" /> with <see cref="Guid" /> keys.
/// </summary>
public class SchemataTenantManager : SchemataTenantManager<SchemataTenant<Guid>, Guid>, ITenantManager
{
    /// <inheritdoc />
    public SchemataTenantManager(
        IRepository<SchemataTenant<Guid>> tenants,
        IRepository<SchemataTenantHost>   hosts,
        ITenantProviderCache              cache
    ) : base(tenants, hosts, cache) { }
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
    private readonly ITenantProviderCache            _cache;
    private readonly IRepository<SchemataTenantHost> _hosts;
    private readonly IRepository<TTenant>            _tenants;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SchemataTenantManager{TTenant, TKey}" /> class.
    /// </summary>
    public SchemataTenantManager(
        IRepository<TTenant>            tenants,
        IRepository<SchemataTenantHost> hosts,
        ITenantProviderCache            cache
    ) {
        _tenants = tenants;
        _hosts   = hosts;
        _cache   = cache;
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
    public virtual async ValueTask<TTenant?> FindByHost(string host, CancellationToken ct) {
        var match = await _hosts.SingleOrDefaultAsync(q => q.Where(h => h.Host == host), ct);
        if (match is null) {
            return null;
        }

        return await _tenants.SingleOrDefaultAsync(q => q.Where(t => t.Id == match.SchemataTenantId), ct);
    }

    /// <inheritdoc />
    public virtual async ValueTask<ImmutableArray<string>> GetHostsAsync(TTenant tenant, CancellationToken ct) {
        var builder = ImmutableArray.CreateBuilder<string>();

        await foreach (var row in _hosts.ListAsync(q => q.Where(h => h.SchemataTenantId == tenant.Id), ct)) {
            if (!string.IsNullOrWhiteSpace(row.Host)) {
                builder.Add(row.Host!);
            }
        }

        return builder.ToImmutable();
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
        TTenant                    tenant,
        Dictionary<string, string> names,
        CancellationToken          ct
    ) {
        tenant.DisplayNames = names is { Count: > 0 } ? names : null;

        return default;
    }

    /// <inheritdoc />
    public virtual async ValueTask SetHostsAsync(TTenant tenant, ImmutableArray<string> hosts, CancellationToken ct) {
        var existing = new List<SchemataTenantHost>();
        await foreach (var row in _hosts.ListAsync(q => q.Where(h => h.SchemataTenantId == tenant.Id), ct)) {
            existing.Add(row);
        }

        if (existing.Count > 0) {
            await _hosts.RemoveRangeAsync(existing, ct);
        }

        if (!hosts.IsDefaultOrEmpty) {
            foreach (var host in hosts) {
                await _hosts.AddAsync(new() { SchemataTenantId = tenant.Id, Host = host }, ct);
            }
        }

        await _hosts.CommitAsync(ct);
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

        // §5.1: a tenant-delete event must evict the cached per-tenant service provider.
        if (tenant.TenantId is { } key) {
            _cache.Remove(key.ToString()!);
        }
    }

    /// <inheritdoc />
    public virtual async ValueTask UpdateAsync(TTenant tenant, CancellationToken ct) {
        await _tenants.UpdateAsync(tenant, ct);
        await _tenants.CommitAsync(ct);
    }

    #endregion
}
