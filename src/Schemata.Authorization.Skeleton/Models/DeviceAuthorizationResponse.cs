namespace Schemata.Authorization.Skeleton.Models;

/// <summary>RFC 8628 device authorization response.</summary>
public class DeviceAuthorizationResponse
{
    /// <summary>Device verification code used by the client to poll for authorization per RFC 8628 section 3.2.</summary>
    public string? DeviceCode { get; set; }

    /// <summary>End-user verification code displayed on the device for manual entry per RFC 8628 section 3.2.</summary>
    public string? UserCode { get; set; }

    /// <summary>URI where the end-user enters the user code per RFC 8628 section 3.2.</summary>
    public string? VerificationUri { get; set; }

    /// <summary>Complete verification URI with the user code pre-filled per RFC 8628 section 3.2.</summary>
    public string? VerificationUriComplete { get; set; }

    /// <summary>Lifetime in seconds of the device and user codes per RFC 8628 section 3.2.</summary>
    public int ExpiresIn { get; set; }

    /// <summary>Minimum polling interval in seconds the client must wait between token requests per RFC 8628 section 3.2.</summary>
    public int Interval { get; set; }
}
