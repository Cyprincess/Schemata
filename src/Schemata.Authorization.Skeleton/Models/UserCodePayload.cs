namespace Schemata.Authorization.Skeleton.Models;

public sealed class UserCodePayload
{
    /// <summary>Reference to the associated device code token entity.</summary>
    public string? DeviceCodeName { get; set; }

    /// <summary>Space-delimited scopes requested in the device authorization.</summary>
    public string? Scope { get; set; }

    /// <summary>Client identifier that initiated the device authorization request.</summary>
    public string? ClientId { get; set; }
}
