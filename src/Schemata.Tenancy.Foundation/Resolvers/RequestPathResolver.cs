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
/// <typeparam name="TKey">The tenant identifier type.</typeparam>
/// <remarks>
///     Returns <see langword="null" /> when no <c>Tenant</c> route value is present.
///     Throws <see cref="TenantResolveException" /> when the value cannot be parsed.
/// </remarks>
public class RequestPathResolver<TKey> : ITenantResolver<TKey>
    where TKey : struct, IEquatable<TKey>, IParsable<TKey>
{
    private readonly IHttpContextAccessor _accessor;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RequestPathResolver{TKey}" /> class.
    /// </summary>
    public RequestPathResolver(IHttpContextAccessor accessor) { _accessor = accessor; }

    #region ITenantResolver<TKey> Members

    /// <inheritdoc />
    public Task<TKey?> ResolveAsync(CancellationToken ct = default) {
        if (_accessor.HttpContext?.Request.RouteValues.TryGetValue("Tenant", out var value) != true) {
            return Task.FromResult<TKey?>(null);
        }

        var id = value?.ToString();
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
