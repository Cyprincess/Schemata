using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Security.Skeleton.Services;

namespace Schemata.Security.Foundation.Signatures;

/// <summary>
///     An HTTP signature algorithm from the registry of RFC 9421 section 6.2: the HTTP_SIGN and
///     HTTP_VERIFY primitives of section 3.3 over the signature base.
/// </summary>
public abstract class HttpSignatureAlgorithm
{
    /// <summary>The registry name, for example <c>ecdsa-p256-sha256</c>.</summary>
    public abstract string Name { get; }

    /// <summary>The HTTP_SIGN primitive: signs the signature base with the signing key material.</summary>
    /// <param name="signatureBase">The ASCII signature base bytes (RFC 9421 section 2.5).</param>
    /// <param name="key">The signing key material from the security store.</param>
    /// <returns>The signature output bytes placed in the Signature field.</returns>
    /// <exception cref="InvalidArgumentException">The key material is not appropriate for this algorithm (RFC 9421 section 3.1 step 1).</exception>
    public abstract byte[] Sign(byte[] signatureBase, SecurityKeyMaterial key);

    /// <summary>The HTTP_VERIFY primitive: verifies a presented signature over the recreated signature base.</summary>
    /// <param name="signatureBase">The recreated ASCII signature base bytes.</param>
    /// <param name="key">The verification key material.</param>
    /// <param name="signature">The presented signature bytes from the Signature field.</param>
    /// <returns><see langword="true" /> when the signature verifies.</returns>
    public abstract bool Verify(byte[] signatureBase, SecurityKeyMaterial key, byte[] signature);

    /// <summary>Whether the algorithm can work with the given key material.</summary>
    /// <param name="key">The key material to test.</param>
    /// <returns><see langword="true" /> when the material is appropriate for this algorithm.</returns>
    public abstract bool Supports(SecurityKeyMaterial key);

    /// <summary>Throws when the material is not appropriate for this algorithm.</summary>
    /// <param name="key">The key material to check.</param>
    /// <exception cref="InvalidArgumentException">The key material is not appropriate for this algorithm.</exception>
    protected void Require(SecurityKeyMaterial key) {
        if (!Supports(key)) {
            throw new InvalidArgumentException(SchemataResources.SIGNATURE_ALGORITHM_KEY_MISMATCH, new Dictionary<string, string?> { ["algorithm"] = Name });
        }
    }
}

/// <summary>HMAC using SHA-256 (RFC 9421 section 3.3.3). Verification compares the recomputed output bytewise.</summary>
public sealed class HmacSha256SignatureAlgorithm : HttpSignatureAlgorithm
{
    /// <inheritdoc />
    public override string Name => SignatureConstants.Algorithms.HmacSha256;

    /// <inheritdoc />
    public override byte[] Sign(byte[] signatureBase, SecurityKeyMaterial key) {
        Require(key);
        var symmetric = (SecurityKeyMaterial.Symmetric)key;
        using var hmac = new HMACSHA256(symmetric.Key);
        return hmac.ComputeHash(signatureBase);
    }

    /// <inheritdoc />
    public override bool Verify(byte[] signatureBase, SecurityKeyMaterial key, byte[] signature) {
        if (!Supports(key)) {
            return false;
        }

        var symmetric = (SecurityKeyMaterial.Symmetric)key;
        using var hmac = new HMACSHA256(symmetric.Key);
        return CryptographicOperations.FixedTimeEquals(hmac.ComputeHash(signatureBase), signature);
    }

    /// <inheritdoc />
    public override bool Supports(SecurityKeyMaterial key) {
        return key is SecurityKeyMaterial.Symmetric;
    }
}

/// <summary>ECDSA using curve P-256 DSS and SHA-256 (RFC 9421 section 3.3.4); the signature is the 64-octet concatenation of r and s, each zero-padded to 32 octets.</summary>
public sealed class EcdsaP256SignatureAlgorithm : HttpSignatureAlgorithm
{
    /// <inheritdoc />
    public override string Name => SignatureConstants.Algorithms.EcdsaP256Sha256;

    /// <inheritdoc />
    public override byte[] Sign(byte[] signatureBase, SecurityKeyMaterial key) {
        Require(key);
        var ec = (SecurityKeyMaterial.EcKey)key;
        return ec.Key.SignData(signatureBase, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    /// <inheritdoc />
    public override bool Verify(byte[] signatureBase, SecurityKeyMaterial key, byte[] signature) {
        if (!Supports(key)) {
            return false;
        }

        var ec = (SecurityKeyMaterial.EcKey)key;
        return ec.Key.VerifyData(signatureBase, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    /// <inheritdoc />
    public override bool Supports(SecurityKeyMaterial key) {
        return key is SecurityKeyMaterial.EcKey { Key.KeySize: 256 };
    }
}

/// <summary>ECDSA using curve P-384 DSS and SHA-384 (RFC 9421 section 3.3.5); the signature is the 96-octet concatenation of r and s, each zero-padded to 48 octets.</summary>
public sealed class EcdsaP384SignatureAlgorithm : HttpSignatureAlgorithm
{
    /// <inheritdoc />
    public override string Name => SignatureConstants.Algorithms.EcdsaP384Sha384;

    /// <inheritdoc />
    public override byte[] Sign(byte[] signatureBase, SecurityKeyMaterial key) {
        Require(key);
        var ec = (SecurityKeyMaterial.EcKey)key;
        return ec.Key.SignData(signatureBase, HashAlgorithmName.SHA384, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    /// <inheritdoc />
    public override bool Verify(byte[] signatureBase, SecurityKeyMaterial key, byte[] signature) {
        if (!Supports(key)) {
            return false;
        }

        var ec = (SecurityKeyMaterial.EcKey)key;
        return ec.Key.VerifyData(signatureBase, signature, HashAlgorithmName.SHA384, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    /// <inheritdoc />
    public override bool Supports(SecurityKeyMaterial key) {
        return key is SecurityKeyMaterial.EcKey { Key.KeySize: 384 };
    }
}

/// <summary>RSASSA-PSS using SHA-512 (RFC 9421 section 3.3.1): MGF1 with SHA-512 and a 64-octet salt, which the runtime PSS padding applies by default.</summary>
public sealed class RsaPssSha512SignatureAlgorithm : HttpSignatureAlgorithm
{
    /// <inheritdoc />
    public override string Name => SignatureConstants.Algorithms.RsaPssSha512;

    /// <inheritdoc />
    public override byte[] Sign(byte[] signatureBase, SecurityKeyMaterial key) {
        Require(key);
        var rsa = (SecurityKeyMaterial.RsaKey)key;
        return rsa.Key.SignData(signatureBase, HashAlgorithmName.SHA512, RSASignaturePadding.Pss);
    }

    /// <inheritdoc />
    public override bool Verify(byte[] signatureBase, SecurityKeyMaterial key, byte[] signature) {
        if (!Supports(key)) {
            return false;
        }

        var rsa = (SecurityKeyMaterial.RsaKey)key;
        return rsa.Key.VerifyData(signatureBase, signature, HashAlgorithmName.SHA512, RSASignaturePadding.Pss);
    }

    /// <inheritdoc />
    public override bool Supports(SecurityKeyMaterial key) {
        return key is SecurityKeyMaterial.RsaKey;
    }
}

/// <summary>RSASSA-PKCS1-v1_5 using SHA-256 (RFC 9421 section 3.3.2).</summary>
public sealed class RsaV15Sha256SignatureAlgorithm : HttpSignatureAlgorithm
{
    /// <inheritdoc />
    public override string Name => SignatureConstants.Algorithms.RsaV15Sha256;

    /// <inheritdoc />
    public override byte[] Sign(byte[] signatureBase, SecurityKeyMaterial key) {
        Require(key);
        var rsa = (SecurityKeyMaterial.RsaKey)key;
        return rsa.Key.SignData(signatureBase, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    /// <inheritdoc />
    public override bool Verify(byte[] signatureBase, SecurityKeyMaterial key, byte[] signature) {
        if (!Supports(key)) {
            return false;
        }

        var rsa = (SecurityKeyMaterial.RsaKey)key;
        return rsa.Key.VerifyData(signatureBase, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    /// <inheritdoc />
    public override bool Supports(SecurityKeyMaterial key) {
        return key is SecurityKeyMaterial.RsaKey;
    }
}

/// <summary>
///     The initial contents of the HTTP Signature Algorithms registry (RFC 9421 sections 3.3 and
///     6.2.2), resolved by name or inferred from key material per section 3.2 step 6.
/// </summary>
/// <remarks>
///     <c>ed25519</c> is a reserved registry name without an implementation: the runtime ships
///     no Ed25519 primitive and the security store has no ed25519 key kind. Resolving it throws
///     like any unregistered name.
/// </remarks>
public static class HttpSignatureAlgorithms
{
    private static readonly IReadOnlyDictionary<string, HttpSignatureAlgorithm> Registry =
        new Dictionary<string, HttpSignatureAlgorithm>(StringComparer.Ordinal) {
            [SignatureConstants.Algorithms.HmacSha256]      = new HmacSha256SignatureAlgorithm(),
            [SignatureConstants.Algorithms.EcdsaP256Sha256] = new EcdsaP256SignatureAlgorithm(),
            [SignatureConstants.Algorithms.EcdsaP384Sha384] = new EcdsaP384SignatureAlgorithm(),
            [SignatureConstants.Algorithms.RsaPssSha512]    = new RsaPssSha512SignatureAlgorithm(),
            [SignatureConstants.Algorithms.RsaV15Sha256]    = new RsaV15Sha256SignatureAlgorithm(),
        };

    /// <summary>The registered algorithms.</summary>
    public static IEnumerable<HttpSignatureAlgorithm> All => Registry.Values;

    /// <summary>Resolves an algorithm by registry name.</summary>
    /// <param name="name">The registry name.</param>
    /// <returns>The algorithm.</returns>
    /// <exception cref="InvalidArgumentException">The name is not registered.</exception>
    public static HttpSignatureAlgorithm Resolve(string name) {
        if (Registry.TryGetValue(name, out var algorithm)) {
            return algorithm;
        }

        throw new InvalidArgumentException(SchemataResources.SIGNATURE_ALGORITHM_UNKNOWN, new Dictionary<string, string?> { ["algorithm"] = name });
    }

    /// <summary>Resolves the algorithm for a signature: an explicit <c>alg</c> name when given, otherwise inferred from the key material (RFC 9421 section 3.2 step 6). RSA material cannot be disambiguated by key alone and defaults to <c>rsa-pss-sha512</c>.</summary>
    /// <param name="name">The explicit <c>alg</c> parameter value, or <see langword="null" />.</param>
    /// <param name="key">The key material.</param>
    /// <returns>The resolved algorithm.</returns>
    /// <exception cref="InvalidArgumentException">The name is unknown, or the material is not appropriate for the resolved algorithm (section 3.1 step 1).</exception>
    public static HttpSignatureAlgorithm Resolve(string? name, SecurityKeyMaterial key) {
        if (name is not null) {
            var algorithm = Resolve(name);
            if (!algorithm.Supports(key)) {
                throw new InvalidArgumentException(SchemataResources.SIGNATURE_ALGORITHM_KEY_MISMATCH, new Dictionary<string, string?> { ["algorithm"] = name });
            }

            return algorithm;
        }

        return key switch {
            SecurityKeyMaterial.Symmetric                  => Registry[SignatureConstants.Algorithms.HmacSha256],
            SecurityKeyMaterial.EcKey { Key.KeySize: 256 } => Registry[SignatureConstants.Algorithms.EcdsaP256Sha256],
            SecurityKeyMaterial.EcKey { Key.KeySize: 384 } => Registry[SignatureConstants.Algorithms.EcdsaP384Sha384],
            SecurityKeyMaterial.RsaKey                     => Registry[SignatureConstants.Algorithms.RsaPssSha512],
            _ => throw new InvalidArgumentException(SchemataResources.SIGNATURE_ALGORITHM_KEY_MISMATCH, new Dictionary<string, string?> { ["algorithm"] = key.GetType().Name }),
        };
    }
}
