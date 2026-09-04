using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Handlers;

/// <summary>
///     Abstract handler for the dynamic client registration endpoint, per
///     <seealso href="https://openid.net/specs/openid-connect-registration-1_0.html">OpenID Connect Dynamic Client Registration 1.0</seealso>
///     .
/// </summary>
public abstract class RegisterEndpoint
{
    /// <summary>Processes a registration request and creates a new client.</summary>
    public abstract Task<RegistrationResponse> HandleAsync(RegisterRequest request, string? bearerToken, CancellationToken ct);
}
