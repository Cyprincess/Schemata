using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Contexts;

/// <summary>
///     Data carrier for the device code exchange pipeline.
///     Consumed by <see cref="Advisors.IDeviceCodeExchangeAdvisor{TApplication}" />.
/// </summary>
public sealed class DeviceCodeExchangeContext<TApplication>
    where TApplication : SchemataApplication
{
    /// <summary>Token endpoint request containing the device code.</summary>
    public TokenRequest? Request { get; set; }

    /// <summary>Resolved client application.</summary>
    public TApplication? Application { get; set; }

    /// <summary>The device code token entity found by resolving the <c>device_code</c> from the request.</summary>
    public SchemataToken? Token { get; set; }
}
