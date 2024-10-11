using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Skeleton;

public interface ITenantManager : ITenantManager<SchemataTenant<Guid>, Guid>;

public interface ITenantManager<TTenant, TKey> where TTenant : SchemataTenant<TKey>
                                               where TKey : struct, IEquatable<TKey>
{
    ValueTask<TTenant?> FindByIdAsync(long id, CancellationToken ct);

    ValueTask<TTenant?> FindByTenantId(TKey identifier, CancellationToken ct);

    ValueTask<TTenant?> FindByHost(string host, CancellationToken ct);

    ValueTask<ImmutableArray<string>> GetHostsAsync(TTenant tenant, CancellationToken ct);

    ValueTask SetTenantId(TTenant tenant, TKey? identifier, CancellationToken ct);

    ValueTask SetDisplayNameAsync(TTenant tenant, string? name, CancellationToken ct);

    ValueTask SetDisplayNamesAsync(
        TTenant                                  tenant,
        ImmutableDictionary<CultureInfo, string> names,
        CancellationToken                        ct);

    ValueTask SetHostsAsync(TTenant tenant, ImmutableArray<string> hosts, CancellationToken ct);

    ValueTask CreateAsync(TTenant tenant, CancellationToken ct);

    ValueTask DeleteAsync(TTenant tenant, CancellationToken ct);

    ValueTask UpdateAsync(TTenant tenant, CancellationToken ct);
}
