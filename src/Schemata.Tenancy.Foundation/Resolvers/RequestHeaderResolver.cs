using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Exceptions;
using Schemata.Tenancy.Skeleton;

namespace Schemata.Tenancy.Foundation.Resolvers;

/// <summary>
///     Resolves the tenant identifier from the <c>x-tenant-id</c> HTTP request header.
/// </summary>
/// <remarks>
///     Returns <see langword="null" /> when the header is absent. Throws <see cref="TenantResolveException" />
///     when the header is present but the value cannot be parsed.
/// </remarks>
public class RequestHeaderResolver : ITenantResolver
{
    private readonly IHttpContextAccessor _accessor;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RequestHeaderResolver" /> class.
    /// </summary>
    public RequestHeaderResolver(IHttpContextAccessor accessor) { _accessor = accessor; }

    #region ITenantResolver Members

    public Task<Guid?> ResolveAsync(CancellationToken ct = default) {
        if (_accessor.HttpContext?.Request.Headers.TryGetValue("x-tenant-id", out var values) != true) {
            return Task.FromResult<Guid?>(null);
        }

        return Task.FromResult<Guid?>(TenantId.Parse(values.FirstOrDefault()));
    }

    #endregion
}
