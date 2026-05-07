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
///     Returns <see langword="null" /> when no <c>Tenant</c> claim is present.
///     Throws <see cref="TenantResolveException" /> when the claim value cannot be parsed.
/// </remarks>
public class RequestPrincipalResolver : ITenantResolver
{
    private readonly IHttpContextAccessor _accessor;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RequestPrincipalResolver" /> class.
    /// </summary>
    public RequestPrincipalResolver(IHttpContextAccessor accessor) { _accessor = accessor; }

    #region ITenantResolver Members

    /// <inheritdoc />
    public Task<Guid?> ResolveAsync(CancellationToken ct = default) {
        var claim = _accessor.HttpContext?.User.FindFirst("Tenant");
        if (claim is null) {
            return Task.FromResult<Guid?>(null);
        }

        var id = claim.Value;
        if (string.IsNullOrWhiteSpace(id)) {
            throw new TenantResolveException();
        }

        if (Guid.TryParse(id, null, out var key)) {
            return Task.FromResult<Guid?>(key);
        }

        throw new TenantResolveException();
    }

    #endregion
}
