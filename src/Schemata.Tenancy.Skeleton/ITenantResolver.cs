using System;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Tenancy.Skeleton;

/// <summary>
///     Resolves the current tenant identifier from the request context.
/// </summary>
public interface ITenantResolver
{
    TenantResolutionStage Stage => TenantResolutionStage.Request;

    /// <summary>Resolves the tenant identifier from the current request, or <see langword="null" /> if absent.</summary>
    Task<Guid?> ResolveAsync(CancellationToken ct);
}
