using System;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Tenancy.Skeleton;

public interface ITenantResolver<TKey> where TKey : struct, IEquatable<TKey>
{
    Task<TKey?> ResolveAsync(CancellationToken ct);
}
