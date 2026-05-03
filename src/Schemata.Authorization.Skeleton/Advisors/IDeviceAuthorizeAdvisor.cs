using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Advisors;

/// <summary>
///     Advisors invoked after client authentication at the device authorization endpoint,
///     before device and user code generation,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc8628.html#section-3.1">
///         RFC 8628: OAuth 2.0 Device Authorization
///         Grant §3.1: Device Authorization Request
///     </seealso>
///     .
/// </summary>
public interface IDeviceAuthorizeAdvisor<TApplication> : IAdvisor<TApplication, DeviceAuthorizeRequest>
    where TApplication : SchemataApplication;
