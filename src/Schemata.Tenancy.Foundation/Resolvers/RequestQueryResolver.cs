using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Exceptions;
using Schemata.Tenancy.Skeleton;

namespace Schemata.Tenancy.Foundation.Resolvers;

/// <summary>
///     Resolves the tenant identifier from the <c>Tenant</c> query string parameter.
/// </summary>
/// <remarks>
///     Returns <see langword="null" /> when the <c>Tenant</c> query parameter is absent.
///     Throws <see cref="TenantResolveException" /> when the value is malformed.
/// </remarks>
public class RequestQueryResolver : ITenantResolver
{
    private readonly IHttpContextAccessor _accessor;

    /// <summary>Creates a resolver that reads from the current query string.</summary>
    public RequestQueryResolver(IHttpContextAccessor accessor) { _accessor = accessor; }

    #region ITenantResolver Members

    public Task<Guid?> ResolveAsync(CancellationToken ct = default) {
        if (_accessor.HttpContext?.Request.Query.TryGetValue("Tenant", out var values) != true) {
            return Task.FromResult<Guid?>(null);
        }

        return Task.FromResult<Guid?>(TenantId.Parse(values.FirstOrDefault()));
    }

    #endregion
}
