using System.Threading;
using System.Threading.Tasks;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Skeleton;

/// <summary>
///     Initializes the request-scoped tenant context.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
/// <remarks>
    ///     Request-pipeline accessors implement this contract. Bound per-tenant accessors expose
    ///     read-only context after the request pipeline resolves the tenant.
/// </remarks>
public interface ITenantContextInitializer<TTenant>
    where TTenant : SchemataTenant
{
    /// <summary>Resolves and initializes the tenant context using the registered <see cref="ITenantResolver" />.</summary>
    Task InitializeAsync(CancellationToken ct);

    /// <summary>Initializes the tenant context with an explicit tenant instance.</summary>
    Task InitializeAsync(TTenant tenant, CancellationToken ct);
}
