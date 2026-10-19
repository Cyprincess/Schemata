using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Tenancy;

namespace Schemata.Resource.Http.Integration.Tests.Fixtures;

/// <summary>Resolves the tenant identifier from the <c>X-Test-Tenant</c> request header.</summary>
public sealed class TestTenantResolver(IHttpContextAccessor accessor) : ITenantResolver
{
    public Task<Guid?> ResolveAsync(CancellationToken ct = default) {
        if (accessor.HttpContext?.Request.Headers.TryGetValue("X-Test-Tenant", out var values) != true) {
            return Task.FromResult<Guid?>(null);
        }

        if (values.Count != 1 || !Guid.TryParse(values[0], out var uid)) {
            throw new TenantResolveException();
        }

        return Task.FromResult<Guid?>(uid);
    }
}
