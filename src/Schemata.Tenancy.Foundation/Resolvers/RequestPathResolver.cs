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
///     Returns <see langword="null" /> when no <c>Tenant</c> route value is present.
///     Throws <see cref="TenantResolveException" /> when the value cannot be parsed.
/// </remarks>
public class RequestPathResolver : ITenantResolver
{
    private readonly IHttpContextAccessor _accessor;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RequestPathResolver" /> class.
    /// </summary>
    public RequestPathResolver(IHttpContextAccessor accessor) { _accessor = accessor; }

    #region ITenantResolver Members

    public Task<Guid?> ResolveAsync(CancellationToken ct = default) {
        if (_accessor.HttpContext?.Request.RouteValues.TryGetValue("Tenant", out var value) != true) {
            return Task.FromResult<Guid?>(null);
        }

        var id = value?.ToString();
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
