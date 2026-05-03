namespace Schemata.Authorization.Skeleton.Models;

/// <summary>
///     Device authorization response,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc8628.html#section-3.2">
///         RFC 8628: OAuth 2.0 Device Authorization
///         Grant §3.2: Device Authorization Response
///     </seealso>
///     .
/// </summary>
public class DeviceAuthorizationResponse
{
    /// <summary>Device verification code used by the client to poll for authorization.</summary>
    public string? DeviceCode { get; set; }

    /// <summary>End-user verification code displayed on the device for manual entry.</summary>
    public string? UserCode { get; set; }

    /// <summary>URI where the end-user enters the user code.</summary>
    public string? VerificationUri { get; set; }

    /// <summary>Complete verification URI with the user code pre-filled.</summary>
    public string? VerificationUriComplete { get; set; }

    /// <summary>Lifetime in seconds of the device and user codes.</summary>
    public int ExpiresIn { get; set; }

    /// <summary>Minimum polling interval in seconds the client must wait between token requests.</summary>
    public int Interval { get; set; }
}
