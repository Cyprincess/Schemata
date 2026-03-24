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
/// <typeparam name="TKey">The tenant identifier type.</typeparam>
/// <remarks>
///     Returns <see langword="null"/> when no <c>Tenant</c> claim is present.
///     Throws <see cref="TenantResolveException" /> when the claim value cannot be parsed.
/// </remarks>
public class RequestPrincipalResolver<TKey> : ITenantResolver<TKey>
    where TKey : struct, IEquatable<TKey>, IParsable<TKey>
{
    private readonly IHttpContextAccessor _accessor;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RequestPrincipalResolver{TKey}" /> class.
    /// </summary>
    public RequestPrincipalResolver(IHttpContextAccessor accessor) { _accessor = accessor; }

    #region ITenantResolver<TKey> Members

    /// <inheritdoc />
    public Task<TKey?> ResolveAsync(CancellationToken ct = default) {
        var claim = _accessor.HttpContext?.User.FindFirst("Tenant");
        if (claim is null) {
            return Task.FromResult<TKey?>(null);
        }

        var id = claim.Value;
        if (string.IsNullOrWhiteSpace(id)) {
            throw new TenantResolveException();
        }

#pragma warning disable CA2252
        if (TKey.TryParse(id, null, out var key))
#pragma warning restore CA2252
        {
            return Task.FromResult<TKey?>(key);
        }

        throw new TenantResolveException();
    }

    #endregion
}
