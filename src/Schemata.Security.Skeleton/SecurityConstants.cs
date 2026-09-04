namespace Schemata.Security.Skeleton;

/// <summary>
///     Well-known constant values for the security material domain.
/// </summary>
public static class SecurityConstants
{
    #region Nested type: Algorithms

    /// <summary>
    ///     Hash formats and key algorithms. Open set: hosts may register their own values.
    /// </summary>
    public static class Algorithms
    {
        /// <summary>PBKDF2 password hashing (default).</summary>
        public const string Pbkdf2 = "pbkdf2";

        /// <summary>bcrypt password hashing.</summary>
        public const string Bcrypt = "bcrypt";

        /// <summary>Argon2id password hashing.</summary>
        public const string Argon2Id = "argon2id";

        /// <summary>RSA.</summary>
        public const string Rsa = "rsa";

        /// <summary>NIST P-256 (secp256r1).</summary>
        public const string P256 = "p-256";

        /// <summary>NIST P-384 (secp384r1).</summary>
        public const string P384 = "p-384";

        /// <summary>NIST P-521 (secp521r1).</summary>
        public const string P521 = "p-521";

        /// <summary>X25519.</summary>
        public const string X25519 = "x25519";

        /// <summary>HMAC with SHA-256.</summary>
        public const string HmacSha256 = "hmac-sha256";
    }

    #endregion

    #region Nested type: Kinds

    /// <summary>
    ///     Material categories stored in a security row.
    /// </summary>
    public static class Kinds
    {
        /// <summary>Plaintext material (API key, HMAC key); stored verbatim.</summary>
        public const string Secret = "secret";

        /// <summary>Password hash; format self-describing or named by <see cref="Algorithms" />.</summary>
        public const string Password = "password";

        /// <summary>Private key (PEM), stored verbatim; import dispatches on
        /// <see cref="Algorithms" /> (rsa / p-256 / p-384 / p-521). JWK-encoded keys use
        /// <see cref="Jwk" /> rows.</summary>
        public const string PrivateKey = "private-key";

        /// <summary>Public key in PEM form.</summary>
        public const string PublicKey = "public-key";

        /// <summary>URI locating a public key.</summary>
        public const string PublicKeyUri = "public-key-uri";

        /// <summary>A single JWK as JSON.</summary>
        public const string Jwk = "jwk";

        /// <summary>A JWKS as JSON.</summary>
        public const string Jwks = "jwks";

        /// <summary>URI locating a JWKS; fetched and cached.</summary>
        public const string JwksUri = "jwks-uri";

        /// <summary>X.509 certificate, reserved for RFC 8705.</summary>
        public const string Certificate = "certificate";
    }

    #endregion

    #region Nested type: Statuses

    /// <summary>
    ///     Lifecycle states of a security row.
    /// </summary>
    public static class Statuses
    {
        /// <summary>The material is active and usable.</summary>
        public const string Valid = "valid";

        /// <summary>The material was rotated out; still verifiable, never re-issued.</summary>
        public const string Retired = "retired";

        /// <summary>The material is revoked; never verifiable again.</summary>
        public const string Revoked = "revoked";

        /// <summary>A token row was consumed (e.g., an exchanged authorization code); never valid again.</summary>
        public const string Redeemed = "redeemed";
    }

    #endregion

    #region Nested type: TokenTypes

    /// <summary>
    ///     Slot-type keys for the unified token store. OAuth row types are registered
    ///     by the authorization packages; the two constant sets converge as raw
    ///     strings at the database.
    /// </summary>
    public static class TokenTypes
    {
        /// <summary>Replay-protection nonce slot.</summary>
        public const string Nonce = "nonce";

        /// <summary>JWT ID (jti) slot.</summary>
        public const string Jti = "jti";

        /// <summary>Rate-limiting slot.</summary>
        public const string RateSlot = "rate-slot";
    }

    #endregion

    #region Nested type: Usages

    /// <summary>
    ///     Purposes a security row serves within its parent. Single words,
    ///     parent-scoped; hosts may register their own values.
    /// </summary>
    public static class Usages
    {
        /// <summary>Authenticating a client or user to the parent resource.</summary>
        public const string Authentication = "authentication";

        /// <summary>Signing tokens or assertions.</summary>
        public const string Signing = "signing";

        /// <summary>Encrypting tokens or payloads.</summary>
        public const string Encryption = "encryption";

        /// <summary>No specialized pipeline; general-purpose material.</summary>
        public const string General = "general";
    }

    #endregion
}
