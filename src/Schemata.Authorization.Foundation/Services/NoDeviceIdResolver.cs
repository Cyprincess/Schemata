using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Services;

namespace Schemata.Authorization.Foundation.Services;

internal sealed class NoDeviceIdResolver : IDeviceIdResolver
{
    public Task<string?> ResolveAsync(ClaimsPrincipal? principal, string? subject, CancellationToken ct = default) {
        return Task.FromResult<string?>(null);
    }
}
