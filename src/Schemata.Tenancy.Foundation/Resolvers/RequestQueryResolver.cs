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
/// <typeparam name="TKey">The tenant identifier type.</typeparam>
/// <remarks>
///     Returns <see langword="null"/> when no <c>Tenant</c> query parameter is present.
///     Throws <see cref="TenantResolveException" /> when the value cannot be parsed.
/// </remarks>
public class RequestQueryResolver<TKey> : ITenantResolver<TKey>
    where TKey : struct, IEquatable<TKey>, IParsable<TKey>
{
    private readonly IHttpContextAccessor _accessor;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RequestQueryResolver{TKey}" /> class.
    /// </summary>
    public RequestQueryResolver(IHttpContextAccessor accessor) { _accessor = accessor; }

    #region ITenantResolver<TKey> Members

    /// <inheritdoc />
    public Task<TKey?> ResolveAsync(CancellationToken ct = default) {
        if (_accessor.HttpContext?.Request.Query.TryGetValue("Tenant", out var values) != true) {
            return Task.FromResult<TKey?>(null);
        }

        var id = values.FirstOrDefault();
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
