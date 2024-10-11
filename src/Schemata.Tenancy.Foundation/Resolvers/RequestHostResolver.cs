using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Exceptions;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Resolvers;

public class RequestHostResolver<TTenant, TKey> : ITenantResolver<TKey> where TTenant : SchemataTenant<TKey>
                                                                        where TKey : struct, IEquatable<TKey>
{
    private readonly IHttpContextAccessor          _accessor;
    private readonly ITenantManager<TTenant, TKey> _manager;

    public RequestHostResolver(IHttpContextAccessor accessor, ITenantManager<TTenant, TKey> manager) {
        _accessor = accessor;
        _manager  = manager;
    }

    #region ITenantResolver<TKey> Members

    public async Task<TKey?> ResolveAsync(CancellationToken ct = default) {
        var hostname = _accessor.HttpContext?.Request.Host.Host;
        if (string.IsNullOrWhiteSpace(hostname)) {
            throw new TenantResolveException();
        }

        var tenant = await _manager.FindByHost(hostname, ct);
        if (tenant is not { TenantId: not null }) {
            throw new TenantResolveException();
        }

        return tenant.TenantId;
    }

    #endregion
}
