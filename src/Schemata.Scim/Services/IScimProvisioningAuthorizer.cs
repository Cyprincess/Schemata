using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Scim.Services;

/// <summary>Authorizes provisioning writes independently from resource reads.</summary>
public interface IScimProvisioningAuthorizer
{
    Task<bool> CanWriteAsync(ClaimsPrincipal principal, string resourceType, string operation, CancellationToken ct);

    Task<bool> CanWritePermissionsAsync(ClaimsPrincipal principal, CancellationToken ct);
}
