using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Handlers;

/// <summary>
///     Abstract handler for the OAuth 2.0 device authorization endpoint,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc8628.html#section-3.1">
///         RFC 8628: OAuth 2.0 Device Authorization
///         Grant §3.1: Device Authorization Request
///     </seealso>
///     .
/// </summary>
public abstract class DeviceAuthorizeEndpoint
{
    /// <summary>Processes a device authorization request.</summary>
    public abstract Task<AuthorizationResult> DeviceAuthorizeAsync(
        DeviceAuthorizeRequest             request,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    );
}
