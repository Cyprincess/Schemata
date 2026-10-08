namespace Schemata.Push.Skeleton;

/// <summary>Push domain constants.</summary>
public static class PushConstants
{
    /// <summary>Default handler key used when no explicit key is registered.</summary>
    public static class Handlers
    {
        /// <summary>The default handler key.</summary>
        public const string Default = "default";
    }

    /// <summary>Web Push (RFC 8030/8291/8292) addressing constants.</summary>
    public static class WebPush
    {
        /// <summary>The provider name identifying Web Push subscription rows.</summary>
        public const string Provider = "webpush";

        /// <summary>The named <see cref="System.Net.Http.HttpClient" /> the transport sends with.</summary>
        public const string HttpClientName = "Schemata.Push.WebPush";

        /// <summary>
        ///     Subscription metadata key carrying the user agent's P-256 ECDH public key (RFC 8291
        ///     section 2.1), base64url-encoded X9.62 uncompressed point, as exposed by the Push API.
        /// </summary>
        public const string PublicKeyMetadata = "p256dh";

        /// <summary>
        ///     Subscription metadata key carrying the user agent's 16-octet authentication secret
        ///     (RFC 8291 section 3.2), base64url-encoded, as exposed by the Push API.
        /// </summary>
        public const string AuthSecretMetadata = "auth";
    }
}
