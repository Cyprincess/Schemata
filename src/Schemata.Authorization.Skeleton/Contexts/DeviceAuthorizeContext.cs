using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Contexts;

/// <summary>
///     Data carrier for the device authorization endpoint pipeline. Populated after client
///     authentication and consumed by <see cref="Advisors.IDeviceAuthorizeAdvisor{TApplication}" />.
/// </summary>
/// <typeparam name="TApplication">The application entity type.</typeparam>
public sealed class DeviceAuthorizeContext<TApplication>
    where TApplication : SchemataApplication
{
    /// <summary>Device authorization request parameters.</summary>
    public DeviceAuthorizeRequest? Request { get; set; }

    /// <summary>Resolved client application.</summary>
    public TApplication? Application { get; set; }

    /// <summary>
    ///     The response written by an advisor returning
    ///     <see cref="Schemata.Abstractions.Advisors.AdviseResult.Handle" />.
    /// </summary>
    public AuthorizationResult? Result { get; set; }
}
