namespace Schemata.Authorization.Skeleton.Models;

/// <summary>Payload serialized inside a device code token.</summary>
public sealed class DeviceCodePayload
{
    /// <summary>Space-delimited scopes associated with this device code.</summary>
    public string? Scope { get; set; }

    /// <summary>Client identifier that initiated the device authorization request.</summary>
    public string? ClientId { get; set; }
}
