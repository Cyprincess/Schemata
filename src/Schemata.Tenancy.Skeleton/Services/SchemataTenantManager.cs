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
///     Repository-backed implementation of <see cref="ITenantManager{TTenant}" />.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public class SchemataTenantManager<TTenant> : ITenantManager<TTenant>
    where TTenant : SchemataTenant
{
    private readonly ITenantProviderCache            _cache;
    private readonly IRepository<SchemataTenantHost> _hosts;
    private readonly IRepository<TTenant>            _tenants;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SchemataTenantManager{TTenant}" /> class.
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

    #region ITenantManager<TTenant> Members

    public virtual ValueTask<TTenant?> FindByTenantId(Guid identifier, CancellationToken ct) {
        return _tenants.SingleOrDefaultAsync(q => q.Where(t => t.Uid.Equals(identifier)), ct);
    }

    public virtual async ValueTask<TTenant?> FindByHost(string host, CancellationToken ct) {
        var match = await _hosts.SingleOrDefaultAsync(q => q.Where(h => h.Host == host), ct);
        if (match is null) {
            return null;
        }

        return await _tenants.SingleOrDefaultAsync(q => q.Where(t => t.Uid == match.SchemataTenantUid), ct);
    }

    public virtual async ValueTask<ImmutableArray<string>> GetHostsAsync(TTenant tenant, CancellationToken ct) {
        var builder = ImmutableArray.CreateBuilder<string>();

        await foreach (var row in _hosts.ListAsync(q => q.Where(h => h.SchemataTenantUid == tenant.Uid), ct)) {
            if (!string.IsNullOrWhiteSpace(row.Host)) {
                builder.Add(row.Host!);
            }
        }

        return builder.ToImmutable();
    }

    public virtual ValueTask SetDisplayNameAsync(TTenant tenant, string? name, CancellationToken ct) {
        tenant.DisplayName = name;
        return default;
    }

    public virtual ValueTask SetDisplayNamesAsync(
        TTenant                    tenant,
        Dictionary<string, string> names,
        CancellationToken          ct
    ) {
        tenant.DisplayNames = names is { Count: > 0 } ? names : null;

        return default;
    }

    public virtual async ValueTask SetHostsAsync(TTenant tenant, ImmutableArray<string> hosts, CancellationToken ct) {
        var existing = new List<SchemataTenantHost>();
        await foreach (var row in _hosts.ListAsync(q => q.Where(h => h.SchemataTenantUid == tenant.Uid), ct)) {
            existing.Add(row);
        }

        if (existing.Count > 0) {
            await _hosts.RemoveRangeAsync(existing, ct);
        }

        if (!hosts.IsDefaultOrEmpty) {
            foreach (var host in hosts) {
                await _hosts.AddAsync(new() { SchemataTenantUid = tenant.Uid, Host = host }, ct);
            }
        }

        await _hosts.CommitAsync(ct);
    }

    public virtual async ValueTask CreateAsync(TTenant tenant, CancellationToken ct) {
        await _tenants.AddAsync(tenant, ct);
        await _tenants.CommitAsync(ct);
    }

    public virtual async ValueTask DeleteAsync(TTenant tenant, CancellationToken ct) {
        await _tenants.RemoveAsync(tenant, ct);
        await _tenants.CommitAsync(ct);

        _cache.Remove(tenant.Uid.ToString()!);
    }

    public virtual async ValueTask UpdateAsync(TTenant tenant, CancellationToken ct) {
        await _tenants.UpdateAsync(tenant, ct);
        await _tenants.CommitAsync(ct);
    }

    #endregion
}
