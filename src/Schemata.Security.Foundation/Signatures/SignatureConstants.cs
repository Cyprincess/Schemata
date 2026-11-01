namespace Schemata.Security.Foundation.Signatures;

/// <summary>
///     Well-known constant values for RFC 9421 HTTP message signatures.
/// </summary>
public static class SignatureConstants
{
    #region Nested type: Components

    /// <summary>
    ///     Derived component names registered by RFC 9421 section 2.2. Names include the
    ///     leading <c>@</c> exactly as they appear in a signature base.
    /// </summary>
    public static class Components
    {
        /// <summary>The HTTP method of a request (RFC 9421 section 2.2.1).</summary>
        public const string Method = "@method";

        /// <summary>The full target URI of a request (RFC 9421 section 2.2.2).</summary>
        public const string TargetUri = "@target-uri";

        /// <summary>The authority of the target URI (RFC 9421 section 2.2.3).</summary>
        public const string Authority = "@authority";

        /// <summary>The scheme of the target URI (RFC 9421 section 2.2.4).</summary>
        public const string Scheme = "@scheme";

        /// <summary>The request target in origin form (RFC 9421 section 2.2.5).</summary>
        public const string RequestTarget = "@request-target";

        /// <summary>The absolute path of the target URI (RFC 9421 section 2.2.6).</summary>
        public const string Path = "@path";

        /// <summary>The query of the target URI, including the leading <c>?</c> (RFC 9421 section 2.2.7).</summary>
        public const string Query = "@query";

        /// <summary>A single named query parameter (RFC 9421 section 2.2.8).</summary>
        public const string QueryParam = "@query-param";

        /// <summary>The three-digit status code of a response (RFC 9421 section 2.2.9).</summary>
        public const string Status = "@status";

        /// <summary>The signature parameters component; always the last line of a signature base (RFC 9421 section 2.3).</summary>
        public const string SignatureParams = "@signature-params";
    }

    #endregion

    #region Nested type: ComponentParameters

    /// <summary>
    ///     Component identifier parameters defined by RFC 9421 sections 2.1 and 2.2.
    /// </summary>
    public static class ComponentParameters
    {
        /// <summary>Boolean flag selecting strict Structured Field serialization (RFC 9421 section 2.1.1).</summary>
        public const string Sf = "sf";

        /// <summary>String parameter selecting a Dictionary Structured Field member (RFC 9421 section 2.1.2).</summary>
        public const string Key = "key";

        /// <summary>Boolean flag wrapping field values as Byte Sequences (RFC 9421 section 2.1.3).</summary>
        public const string Bs = "bs";

        /// <summary>Boolean flag pulling the value from the related request of a response target (RFC 9421 section 2.4).</summary>
        public const string Req = "req";

        /// <summary>Boolean flag pulling the value from trailer fields (RFC 9421 section 2.1.4).</summary>
        public const string Tr = "tr";

        /// <summary>String parameter naming a query parameter for <c>@query-param</c> (RFC 9421 section 2.2.8).</summary>
        public const string Name = "name";
    }

    #endregion

    #region Nested type: Parameters

    /// <summary>
    ///     Signature metadata parameters defined by RFC 9421 section 2.3.
    /// </summary>
    public static class Parameters
    {
        /// <summary>Creation time as a UNIX timestamp Integer.</summary>
        public const string Created = "created";

        /// <summary>Expiration time as a UNIX timestamp Integer.</summary>
        public const string Expires = "expires";

        /// <summary>A random unique value as a String.</summary>
        public const string Nonce = "nonce";

        /// <summary>The signature algorithm from the HTTP Signature Algorithms registry, as a String.</summary>
        public const string Alg = "alg";

        /// <summary>The identifier for the verification key material, as a String.</summary>
        public const string KeyId = "keyid";

        /// <summary>An application-specific tag, as a String.</summary>
        public const string Tag = "tag";
    }

    #endregion

    #region Nested type: Fields

    /// <summary>
    ///     HTTP field names carrying message signatures and content digests.
    /// </summary>
    public static class Fields
    {
        /// <summary>The Signature-Input field (RFC 9421 section 4.1).</summary>
        public const string SignatureInput = "signature-input";

        /// <summary>The Signature field (RFC 9421 section 4.2).</summary>
        public const string Signature = "signature";

        /// <summary>The Content-Digest field (RFC 9530), covered when the body participates.</summary>
        public const string ContentDigest = "content-digest";
    }

    #endregion

    #region Nested type: Algorithms

    /// <summary>
    ///     Initial contents of the HTTP Signature Algorithms registry (RFC 9421 section 6.2.2).
    /// </summary>
    public static class Algorithms
    {
        /// <summary>RSASSA-PSS using SHA-512, MGF1 with SHA-512, 64-octet salt (RFC 9421 section 3.3.1).</summary>
        public const string RsaPssSha512 = "rsa-pss-sha512";

        /// <summary>RSASSA-PKCS1-v1_5 using SHA-256 (RFC 9421 section 3.3.2).</summary>
        public const string RsaV15Sha256 = "rsa-v1_5-sha256";

        /// <summary>HMAC using SHA-256 (RFC 9421 section 3.3.3).</summary>
        public const string HmacSha256 = "hmac-sha256";

        /// <summary>ECDSA using curve P-256 DSS and SHA-256 (RFC 9421 section 3.3.4).</summary>
        public const string EcdsaP256Sha256 = "ecdsa-p256-sha256";

        /// <summary>ECDSA using curve P-384 DSS and SHA-384 (RFC 9421 section 3.3.5).</summary>
        public const string EcdsaP384Sha384 = "ecdsa-p384-sha384";

        /// <summary>EdDSA using curve edwards25519 (RFC 9421 section 3.3.6). Reserved name; the runtime ships no Ed25519 primitive and the security store has no ed25519 key kind, so the registry cannot resolve it.</summary>
        public const string Ed25519 = "ed25519";
    }

    #endregion

    #region Nested type: DigestAlgorithms

    /// <summary>
    ///     Hash algorithm names for the Content-Digest field (RFC 9530 section 3).
    /// </summary>
    public static class DigestAlgorithms
    {
        /// <summary>The sha-256 content digest.</summary>
        public const string Sha256 = "sha-256";

        /// <summary>The sha-512 content digest.</summary>
        public const string Sha512 = "sha-512";
    }

    #endregion
}
