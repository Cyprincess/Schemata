using System;

namespace Schemata.Authorization.Foundation.Authentication;

/// <summary>Configuration for OpenID Connect Native Single Sign-On.</summary>
public sealed class NativeSingleSignOnOptions
{
    /// <summary>Lifetime of an issued device secret.</summary>
    public TimeSpan DeviceSecretLifetime { get; set; } = TimeSpan.FromDays(14);
}
