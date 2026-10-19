using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Tenancy.Skeleton;

namespace Schemata.Resource.Grpc.Integration.Tests.Fixtures;

/// <summary>Resolves the tenant identifier from the <c>X-Test-Tenant</c> request header.</summary>
public sealed class TestTenantResolver(IHttpContextAccessor accessor) : ITenantResolver
{
    public Task<Guid?> ResolveAsync(CancellationToken ct = default) {
        if (accessor.HttpContext?.Request.Headers.TryGetValue("X-Test-Tenant", out var values) != true) {
            return Task.FromResult<Guid?>(null);
        }

        return Task.FromResult<Guid?>(Guid.TryParse(values[0], out var uid) ? uid : null);
    }
}
