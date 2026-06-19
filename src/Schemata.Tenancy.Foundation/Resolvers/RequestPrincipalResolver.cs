using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Exceptions;
using Schemata.Tenancy.Skeleton;

namespace Schemata.Tenancy.Foundation.Resolvers;

/// <summary>
///     Resolves the tenant identifier from the <c>Tenant</c> claim on the authenticated principal.
/// </summary>
/// <remarks>
///     Returns <see langword="null" /> when the <c>Tenant</c> claim is absent.
///     Throws <see cref="TenantResolveException" /> when the claim value is malformed.
/// </remarks>
public class RequestPrincipalResolver : ITenantResolver
{
    private readonly IHttpContextAccessor _accessor;

    /// <summary>Creates a resolver that reads from the current authenticated principal.</summary>
    public RequestPrincipalResolver(IHttpContextAccessor accessor) { _accessor = accessor; }

    #region ITenantResolver Members

    public Task<Guid?> ResolveAsync(CancellationToken ct = default) {
        var claim = _accessor.HttpContext?.User.FindFirst("Tenant");
        if (claim is null) {
            return Task.FromResult<Guid?>(null);
        }

        return Task.FromResult<Guid?>(TenantId.Parse(claim.Value));
    }

    #endregion
}
