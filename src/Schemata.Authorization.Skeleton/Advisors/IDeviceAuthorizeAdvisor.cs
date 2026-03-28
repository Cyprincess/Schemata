using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Advisors;

/// <summary>
///     Runs after client authentication at the device authorization endpoint (RFC 8628 §3.1), before device/user code
///     generation.
/// </summary>
public interface IDeviceAuthorizeAdvisor<TApplication> : IAdvisor<TApplication, DeviceAuthorizeRequest>
    where TApplication : SchemataApplication;
