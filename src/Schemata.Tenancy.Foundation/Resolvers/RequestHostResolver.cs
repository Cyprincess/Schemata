using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Exceptions;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Resolvers;

public class RequestHostResolver<TTenant, TKey>(IHttpContextAccessor accessor, ITenantManager<TTenant, TKey> manager) : ITenantResolver<TKey>
    where TTenant : SchemataTenant<TKey>
    where TKey : struct, IEquatable<TKey>
{
    #region ITenantResolver<TKey> Members

    public async Task<TKey?> ResolveAsync(CancellationToken ct = default) {
        var hostname = accessor.HttpContext?.Request.Host.Host;
        if (string.IsNullOrWhiteSpace(hostname)) {
            throw new TenantResolveException();
        }

        var tenant = await manager.FindByHost(hostname, ct);
        if (tenant is not { TenantId: not null }) {
            throw new TenantResolveException();
        }

        return tenant.TenantId;
    }

    #endregion
}
