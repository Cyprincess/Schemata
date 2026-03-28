using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Skeleton;

/// <summary>
///     Manages tenant entities using the default <see cref="SchemataTenant{TKey}" /> with <see cref="Guid" /> keys.
/// </summary>
public interface ITenantManager : ITenantManager<SchemataTenant<Guid>, Guid>;

/// <summary>
///     Provides CRUD operations and lookup methods for tenant entities.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
/// <typeparam name="TKey">The tenant identifier type.</typeparam>
public interface ITenantManager<TTenant, TKey>
    where TTenant : SchemataTenant<TKey>
    where TKey : struct, IEquatable<TKey>
{
    /// <summary>Finds a tenant by its primary key.</summary>
    ValueTask<TTenant?> FindByIdAsync(long id, CancellationToken ct);

    /// <summary>Finds a tenant by its tenant-specific identifier.</summary>
    ValueTask<TTenant?> FindByTenantId(TKey identifier, CancellationToken ct);

    /// <summary>Finds a tenant by a host name.</summary>
    ValueTask<TTenant?> FindByHost(string host, CancellationToken ct);

    /// <summary>Gets the host names associated with the tenant.</summary>
    ValueTask<ImmutableArray<string>> GetHostsAsync(TTenant tenant, CancellationToken ct);

    /// <summary>Sets the tenant-specific identifier.</summary>
    ValueTask SetTenantId(TTenant tenant, TKey? identifier, CancellationToken ct);

    /// <summary>Sets the display name.</summary>
    ValueTask SetDisplayNameAsync(TTenant tenant, string? name, CancellationToken ct);

    /// <summary>Sets the localized display names.</summary>
    ValueTask SetDisplayNamesAsync(TTenant tenant, Dictionary<string, string> names, CancellationToken ct);

    /// <summary>Sets the host names associated with the tenant.</summary>
    ValueTask SetHostsAsync(TTenant tenant, ImmutableArray<string> hosts, CancellationToken ct);

    /// <summary>Creates a new tenant.</summary>
    ValueTask CreateAsync(TTenant tenant, CancellationToken ct);

    /// <summary>Deletes a tenant.</summary>
    ValueTask DeleteAsync(TTenant tenant, CancellationToken ct);

    /// <summary>Updates an existing tenant.</summary>
    ValueTask UpdateAsync(TTenant tenant, CancellationToken ct);
}
