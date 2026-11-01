using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Skeleton;

/// <summary>
///     Creates isolated <see cref="System.IServiceProvider" /> instances scoped to a specific tenant.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
/// <remarks>
///     <para>
///         Tenant-specific and dynamic registrations form each versioned container.
///         Host registrations retain host construction and ownership.
///     </para>
///     <para>
    ///         The returned <see cref="ITenantProviderLease" /> pins the cached provider while in use;
    ///         callers must dispose the lease when the tenant scope it backs is disposed.
///     </para>
/// </remarks>
public interface ITenantServiceProviderFactory<TTenant>
    where TTenant : SchemataTenant
{
    ValueTask<ITenantProviderLease> CreateServiceProviderAsync(Guid identifier, CancellationToken ct = default);
}
