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
    public TenantResolutionStage Stage => TenantResolutionStage.Principal;

    private readonly IHttpContextAccessor _accessor;

    /// <summary>Creates a resolver that reads from the current authenticated principal.</summary>
    public RequestPrincipalResolver(IHttpContextAccessor accessor) { _accessor = accessor; }

    #region ITenantResolver Members

    public Task<Guid?> ResolveAsync(CancellationToken ct = default) {
        Guid? selected = null;
        if (_accessor.HttpContext is not { } http) return Task.FromResult(selected);
        foreach (var identity in http.User.Identities) {
            if (!identity.IsAuthenticated) continue;
            foreach (var claim in identity.FindAll("Tenant")) {
                var candidate = TenantId.Parse(claim.Value);
                if (selected is { } current && current != candidate) throw new TenantResolveException();
                selected = candidate;
            }
        }
        return Task.FromResult(selected);
    }

    #endregion
}
