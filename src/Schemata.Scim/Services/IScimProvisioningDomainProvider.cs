using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Scim.Services;

/// <summary>Resolves the provisioning domain represented by an authenticated caller.</summary>
public interface IScimProvisioningDomainProvider
{
    Task<string> GetDomainAsync(ClaimsPrincipal principal, CancellationToken ct);
}
