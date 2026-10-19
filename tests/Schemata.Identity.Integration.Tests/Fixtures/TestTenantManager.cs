using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Identity.Integration.Tests.Fixtures;

/// <summary>
///     Resolves every requested identifier as an existing tenant with a stable version stamp, so
///     per-tenant provider leases stay valid across requests in the integration host.
/// </summary>
public sealed class TestTenantManager : ITenantManager<SchemataTenant>
{
    // Shared across scopes: a per-scope dictionary would mint a new version stamp per request
    // and force a fresh tenant provider on every call.
    private static readonly ConcurrentDictionary<Guid, SchemataTenant> Tenants = new();

    public ValueTask<SchemataTenant?> FindByTenantId(Guid identifier, CancellationToken ct) {
        return new ValueTask<SchemataTenant?>((SchemataTenant?)Tenants.GetOrAdd(identifier, static value => new SchemataTenant { Uid = value, Timestamp = Guid.NewGuid(), Name = value.ToString() }));
    }

    public ValueTask<SchemataTenant?> FindByHost(string host, CancellationToken ct) { return new ValueTask<SchemataTenant?>((SchemataTenant?)null); }

    public ValueTask<ImmutableArray<string>> GetHostsAsync(SchemataTenant tenant, CancellationToken ct) {
        return new(ImmutableArray<string>.Empty);
    }

    public ValueTask SetDisplayNameAsync(SchemataTenant tenant, string? name, CancellationToken ct) {
        tenant.Name = name;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetDisplayNamesAsync(SchemataTenant tenant, Dictionary<string, string?> names, CancellationToken ct) {
        return ValueTask.CompletedTask;
    }

    public ValueTask SetHostsAsync(SchemataTenant tenant, ImmutableArray<string> hosts, CancellationToken ct) {
        return ValueTask.CompletedTask;
    }

    public ValueTask CreateAsync(SchemataTenant tenant, CancellationToken ct) {
        Tenants[tenant.Uid] = tenant;
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteAsync(SchemataTenant tenant, CancellationToken ct) {
        Tenants.TryRemove(tenant.Uid, out _);
        return ValueTask.CompletedTask;
    }

    public ValueTask UpdateAsync(SchemataTenant tenant, CancellationToken ct) {
        Tenants[tenant.Uid] = tenant;
        return ValueTask.CompletedTask;
    }
}
