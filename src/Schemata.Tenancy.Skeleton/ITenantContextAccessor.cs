using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Skeleton;

public interface ITenantContextAccessor : ITenantContextAccessor<SchemataTenant<Guid>, Guid>;

public interface ITenantContextAccessor<TTenant, TKey>
    where TTenant : SchemataTenant<TKey>
    where TKey : struct, IEquatable<TKey>
{
    TTenant? Tenant { get; }

    Task InitializeAsync(CancellationToken ct);

    Task InitializeAsync(TTenant tenant, CancellationToken ct);

    Task<IServiceProvider> GetBaseServiceProviderAsync(CancellationToken ct);
}
