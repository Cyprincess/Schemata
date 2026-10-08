using System;

namespace Schemata.Push.Foundation.WebPush;

/// <summary>
///     Configuration for the built-in Web Push delivery provider: the VAPID key pair and contact
///     used for application-server self-identification (RFC 8292 section 2) plus delivery defaults.
/// </summary>
public sealed class WebPushOptions
{
    /// <summary>Longest permitted <see cref="TokenLifetime" /> (RFC 8292 section 2).</summary>
    public static readonly TimeSpan MaxTokenLifetime = TimeSpan.FromHours(24);

    /// <summary>
    ///     Contact URI for the application server, carried as the JWT <c>sub</c> claim
    ///     (RFC 8292 section 2.1); a <c>mailto:</c> or <c>https:</c> URI. The claim is omitted when
    ///     unset.
    /// </summary>
    public string? Subject { get; set; }

    /// <summary>
    ///     VAPID public key: the base64url-encoded X9.62 uncompressed P-256 point, sent as the
    ///     <c>k</c> parameter of the <c>vapid</c> authorization scheme (RFC 8292 section 3.2).
    /// </summary>
    public string? VapidPublicKey { get; set; }

    /// <summary>
    ///     VAPID private key: the base64url-encoded 32-octet P-256 scalar signing the JWT
    ///     (ES256, RFC 8292 section 2).
    /// </summary>
    public string? VapidPrivateKey { get; set; }

    /// <summary>
    ///     Lifetime of each VAPID JWT. Must not exceed <see cref="MaxTokenLifetime" />.
    ///     Defaults to 12 hours.
    /// </summary>
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    ///     TTL header value sent when a dispatch carries no
    ///     <see cref="Skeleton.PushOptions.TimeToLive" /> (RFC 8030 section 5.2 requires the
    ///     header). Defaults to four weeks.
    /// </summary>
    public TimeSpan DefaultTimeToLive { get; set; } = TimeSpan.FromDays(28);
}
