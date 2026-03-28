namespace Schemata.Authorization.Skeleton.Models;

public sealed class DeviceCodePayload
{
    /// <summary>Space-delimited scopes associated with this device code.</summary>
    public string? Scope { get; set; }

    /// <summary>Client identifier that initiated the device authorization request.</summary>
    public string? ClientId { get; set; }
}
