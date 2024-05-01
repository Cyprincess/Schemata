using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Exceptions;
using Schemata.Tenancy.Skeleton;

namespace Schemata.Tenancy.Foundation.Resolvers;

public class RequestQueryResolver<TKey>(IHttpContextAccessor accessor) : ITenantResolver<TKey>
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
        if (accessor.HttpContext?.Request.Query.TryGetValue("Tenant", out var values) != true) {
            return Task.FromResult<TKey?>(default);
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
