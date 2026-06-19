using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Exceptions;
using Schemata.Tenancy.Skeleton;

namespace Schemata.Tenancy.Foundation.Resolvers;

/// <summary>
///     Resolves the tenant identifier from the <c>{Tenant}</c> route parameter.
/// </summary>
/// <remarks>
///     Returns <see langword="null" /> when the <c>Tenant</c> route value is absent.
///     Throws <see cref="TenantResolveException" /> when the value is malformed.
/// </remarks>
public class RequestPathResolver : ITenantResolver
{
    private readonly IHttpContextAccessor _accessor;

    /// <summary>Creates a resolver that reads from the current route values.</summary>
    public RequestPathResolver(IHttpContextAccessor accessor) { _accessor = accessor; }

    #region ITenantResolver Members

    public Task<Guid?> ResolveAsync(CancellationToken ct = default) {
        if (_accessor.HttpContext?.Request.RouteValues.TryGetValue("Tenant", out var value) != true) {
            return Task.FromResult<Guid?>(null);
        }

        return Task.FromResult<Guid?>(TenantId.Parse(value?.ToString()));
    }

    #endregion
}
