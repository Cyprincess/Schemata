namespace Schemata.Authorization.Skeleton.Models;

/// <summary>Payload serialized inside a user code token.</summary>
public sealed class UserCodePayload
{
    /// <summary>Reference to the associated device code token.</summary>
    public string? DeviceCodeName { get; set; }

    /// <summary>Space-delimited scopes requested in the device authorization.</summary>
    public string? Scope { get; set; }

    /// <summary>Client identifier that initiated the device authorization request.</summary>
    public string? ClientId { get; set; }
}
