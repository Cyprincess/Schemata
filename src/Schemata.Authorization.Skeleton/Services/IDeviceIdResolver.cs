using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Authorization.Skeleton.Services;

public interface IDeviceIdResolver
{
    Task<string?> ResolveAsync(ClaimsPrincipal? principal, string? subject, CancellationToken ct = default);
}