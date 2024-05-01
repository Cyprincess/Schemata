using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Exceptions;
using Schemata.Tenancy.Skeleton;

namespace Schemata.Tenancy.Foundation.Resolvers;

public class RequestPathResolver<TKey>(IHttpContextAccessor accessor) : ITenantResolver<TKey>
#if NET8_0_OR_GREATER
    where TKey : struct, IEquatable<TKey>, IParsable<TKey>
#else
#pragma warning disable CA2252
    where TKey : struct, IEquatable<TKey>, IParseable<TKey>
#pragma warning restore CA2252
#endif
{
    #region ITenantResolver Members

    public Task<TKey?> ResolveAsync(CancellationToken ct = default) {
        if (accessor.HttpContext?.Request.RouteValues.TryGetValue("Tenant", out var value) != true) {
            return Task.FromResult<TKey?>(default);
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
