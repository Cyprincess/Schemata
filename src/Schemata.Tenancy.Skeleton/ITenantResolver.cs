using System;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Tenancy.Skeleton;

/// <summary>
///     Resolves the current tenant identifier from the request context.
/// </summary>
/// <remarks>
///     Implementations extract the tenant identifier from different request sources
///     (header, host, path, query string, principal claims). Register one implementation
///     per application via the <c>SchemataTenancyBuilder</c>.
/// </remarks>
public interface ITenantResolver
{
    /// <summary>Resolves the tenant identifier from the current request, or <see langword="null" /> if absent.</summary>
    Task<Guid?> ResolveAsync(CancellationToken ct);
}
