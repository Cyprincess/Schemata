using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Contexts;

/// <summary>
///     Data carrier for the device code exchange pipeline.
///     Consumed by <see cref="Advisors.IDeviceCodeExchangeAdvisor{TApplication, TToken}" />.
/// </summary>
public sealed class DeviceCodeExchangeContext<TApplication, TToken>
    where TApplication : SchemataApplication
    where TToken : SchemataToken
{
    /// <summary>Token endpoint request containing the device code.</summary>
    public TokenRequest? Request { get; set; }

    /// <summary>Resolved client application.</summary>
    public TApplication? Application { get; set; }

    /// <summary>The device code token entity found by resolving the <c>device_code</c> from the request.</summary>
    public TToken? Token { get; set; }
}
