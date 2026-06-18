using System.Threading;
using System.Threading.Tasks;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Skeleton;

/// <summary>
///     Initializes the request-scoped tenant context.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
/// <remarks>
///     Implemented only by accessors that resolve the tenant during the request pipeline. Accessors
///     bound to an already-resolved tenant inside a per-tenant scope are read-only and intentionally
///     do not implement this, so an initialization call on a bound context fails to compile rather
///     than silently doing nothing.
/// </remarks>
public interface ITenantContextInitializer<TTenant>
    where TTenant : SchemataTenant
{
    /// <summary>Resolves and initializes the tenant context using the registered <see cref="ITenantResolver" />.</summary>
    Task InitializeAsync(CancellationToken ct);

    /// <summary>Initializes the tenant context with an explicit tenant instance.</summary>
    Task InitializeAsync(TTenant tenant, CancellationToken ct);
}
