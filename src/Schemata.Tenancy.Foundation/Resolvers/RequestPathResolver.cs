using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Exceptions;
using Schemata.Tenancy.Skeleton;

namespace Schemata.Tenancy.Foundation.Resolvers;

public class RequestPathResolver<TKey> : ITenantResolver<TKey>
    where TKey : struct, IEquatable<TKey>, IParsable<TKey>
{
    private readonly IHttpContextAccessor _accessor;

    public RequestPathResolver(IHttpContextAccessor accessor) {
        _accessor = accessor;
    }

    #region ITenantResolver Members

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
