using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Exceptions;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Resolvers;

/// <summary>
///     Resolves the tenant by matching the request <c>Host</c> header against tenant host names.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
/// <remarks>
///     Looks up the tenant via <see cref="ITenantManager{TTenant}.FindByHost" />.
///     Throws <see cref="TenantResolveException" /> when no matching tenant is found.
/// </remarks>
public class RequestHostResolver<TTenant> : ITenantResolver
    where TTenant : SchemataTenant
{
    private readonly IHttpContextAccessor    _accessor;
    private readonly ITenantManager<TTenant> _manager;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RequestHostResolver{TTenant}" /> class.
    /// </summary>
    public RequestHostResolver(IHttpContextAccessor accessor, ITenantManager<TTenant> manager) {
        _accessor = accessor;
        _manager  = manager;
    }

    #region ITenantResolver Members

    public async Task<Guid?> ResolveAsync(CancellationToken ct = default) {
        var hostname = _accessor.HttpContext?.Request.Host.Host;
        if (string.IsNullOrWhiteSpace(hostname)) {
            throw new TenantResolveException();
        }

        var tenant = await _manager.FindByHost(hostname, ct);
        if (tenant is null) {
            throw new TenantResolveException();
        }

        return tenant.Uid;
    }

    #endregion
}
