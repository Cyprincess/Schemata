namespace Schemata.Authorization.Skeleton.Models;

public class DeviceAuthorizeRequest
{
    /// <summary>OAuth 2.0 client identifier per RFC 6749 section 2.2.</summary>
    public string? ClientId { get; set; }

    /// <summary>Client secret for confidential clients, sent in the request body per RFC 6749 section 2.3.1.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Space-delimited scopes requested for the device authorization per RFC 8628 section 3.1.</summary>
    public string? Scope { get; set; }
}
